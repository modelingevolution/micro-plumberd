using KurrentDB.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MicroPlumberd.Migration;

/// <summary>The outcome of a full migration run (dry or real).</summary>
public sealed class MigrationRunResult
{
    /// <summary>True when the run wrote nothing to the destination.</summary>
    public required bool DryRun { get; init; }

    /// <summary>Ids of the migrations that were pending and applied (in order).</summary>
    public required IReadOnlyList<string> PendingMigrationIds { get; init; }

    /// <summary>The copy engine's result (per-migration and per-stream accounting).</summary>
    public required CopyResult Copy { get; init; }

    /// <summary>The verification report — <c>null</c> on a dry run (nothing was written).</summary>
    public VerificationReport? Verification { get; init; }

    /// <summary>The history records appended to the destination — empty on a dry run.</summary>
    public required IReadOnlyList<MigrationApplied> NewlyApplied { get; init; }

    /// <summary>Names of the source user projections pre-created on the dest (empty when no projection copy ran).</summary>
    public IReadOnlyList<string> CopiedProjections { get; init; } = [];

    /// <summary>
    /// User-defined indexes created on the destination to serve the <c>[OutputStream]</c> merges in commit order
    /// (empty unless a <see cref="UserDefinedIndexCopyContext"/> ran). <b>REWIRING REQUIRED:</b> the physical
    /// merge streams are NOT rebuilt — for each entry a consumer must read
    /// <see cref="CreatedIndex.IndexStream"/> (<c>$idx-user-…</c>) INSTEAD of <see cref="CreatedIndex.OutputStream"/>
    /// (which is left empty on the dest). See <see cref="UserDefinedIndexCopyContext"/>.
    /// </summary>
    public IReadOnlyList<CreatedIndex> CreatedIndexes { get; init; } = [];
}

/// <summary>
/// Enables the PROJECTION COPY (the owner's <c>[OutputStream]</c> recovery design). When supplied to
/// <see cref="MigrationRunner.RunAsync"/>, the runner pre-creates the app's <c>linkTo</c> join projections on
/// the empty destination and PACES the event copy so each projection stays caught up event-by-event (never a
/// backlog), rebuilding every merge stream in commit order; the app NO-OPs on boot via the reproduced
/// <c>mp_query_hash</c>. When omitted, the runner runs the simple copy (merge streams are skipped and the app
/// regenerates them on boot — order-correct only if the app's projection is itself commit-ordered).
/// </summary>
public sealed class ProjectionCopyContext
{
    /// <summary>Projection-management client for the SOURCE store (read the user projections + their queries).</summary>
    public required KurrentDBProjectionManagementClient SourceProjections { get; init; }

    /// <summary>Projection-management client for the DEST store (create + drain the live projections).</summary>
    public required KurrentDBProjectionManagementClient DestProjections { get; init; }

    /// <summary>SOURCE connection string — used to read each projection's query verbatim over HTTP.</summary>
    public required string SourceConnectionString { get; init; }

    /// <summary>Per-event pacing timeout (max wait for one link to be emitted). Default 30s.</summary>
    public TimeSpan PerEventPaceTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Final per-projection drain timeout. Default 60s.</summary>
    public TimeSpan DrainTimeout { get; init; } = TimeSpan.FromSeconds(60);
}

/// <summary>
/// Orchestrates an offline migration run: read history, guard checksums, compute the pending set, copy
/// SOURCE→DEST applying the pending migrations, then (for a real run) record history and verify.
/// </summary>
public sealed class MigrationRunner(ILoggerFactory? loggerFactory = null)
{
    private readonly ILoggerFactory _lf = loggerFactory ?? NullLoggerFactory.Instance;

    /// <summary>
    /// Live status + per-phase progress of this runner's current/last <see cref="RunAsync"/>. Subscribe to
    /// <see cref="MigrationProgressTracker.ProgressChanged"/> (or the <see cref="IObservable{T}"/>) for live
    /// updates, or read <see cref="MigrationProgressTracker.CurrentStatus"/> at any time (thread-safe). One
    /// runner instance drives one run — hold it in the migrator app and serialize <c>CurrentStatus</c> in REST.
    /// </summary>
    public MigrationProgressTracker Progress { get; } = new();

    /// <summary>Shorthand for <c>Progress.CurrentStatus</c> — the thread-safe status snapshot for a REST layer.</summary>
    public MigrationStatusSnapshot CurrentStatus => Progress.CurrentStatus;

    /// <summary>
    /// Runs the migration. <paramref name="source"/> is read-only; <paramref name="dest"/> must be a fresh
    /// (empty) store. When <paramref name="dryRun"/> is true, nothing is written to <paramref name="dest"/>.
    /// Progress is reported through <see cref="Progress"/> throughout the run.
    /// </summary>
    public async Task<MigrationRunResult> RunAsync(
        KurrentDBClient source,
        KurrentDBClient dest,
        IReadOnlyList<Migration> migrations,
        bool dryRun,
        ProjectionCopyContext? projectionCopy = null,
        UserDefinedIndexCopyContext? indexCopy = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(dest);
        ArgumentNullException.ThrowIfNull(migrations);
        if (projectionCopy is not null && indexCopy is not null)
            throw new InvalidOperationException(
                "ProjectionCopyContext and UserDefinedIndexCopyContext are mutually exclusive merge-recovery "
                + "strategies — supply at most one. The projection copy PACES a rebuild of the physical merge "
                + "streams; the index copy creates user-defined indexes read in commit order.");

        var logger = _lf.CreateLogger<MigrationRunner>();
        var runTimeUtc = DateTime.UtcNow;
        var reserved = new HashSet<string>(StringComparer.Ordinal) { MigrationHistory.StreamName };

        Progress.BeginRun(dryRun, runTimeUtc);
        try
        {
            // 1. Order defined migrations (rejects duplicate Ids) and compile their rules.
            var defined = MigrationDiscovery.Order(migrations).Select(CompiledMigration.Compile).ToList();

            // 2. Read applied history and enforce the checksum guard.
            var history = new MigrationHistory(_lf.CreateLogger<MigrationHistory>());
            var applied = await history.ReadAsync(source, ct).ConfigureAwait(false);
            var appliedById = applied.ToDictionary(a => a.Id, StringComparer.Ordinal);
            foreach (var m in defined)
            {
                if (appliedById.TryGetValue(m.Id, out var a) && a.Checksum != m.Checksum)
                    throw new MigrationChecksumMismatchException(m.Id, a.Checksum, m.Checksum);
            }
            foreach (var a in applied)
                if (defined.All(d => d.Id != a.Id))
                    logger.LogWarning("History contains applied migration '{Id}' not present in code — its "
                                      + "effects are already baked into the source and it will be carried forward.", a.Id);

            // 3. Pending = defined not yet applied, in Id order.
            var pending = defined.Where(d => !appliedById.ContainsKey(d.Id)).ToList();
            var plan = new MigrationPlan(pending);
            Progress.SetMigrations(
                applied.Select(a => (a.Id, a.Name)),
                pending.Select(p => (p.Id, p.Name)));

            // 4. Reserved/system guard (m8a): no rule may target the reserved history stream, and no rule may
            //    retarget an event into $-space — a RenameStream into $… or a RenameType to a $-typed name would
            //    be silently SKIPPED by the copy engine (system-namespace), making those events vanish.
            static bool IsSystem(string n) => n.Length > 0 && n[0] == '$';
            var offending = plan.ReferencedStreamNames.Concat(plan.RenameStreamTargets)
                .Where(n => reserved.Contains(n) || IsSystem(n)).Distinct().ToList();
            var offendingTypes = plan.RenameTypeTargets.Where(IsSystem).Distinct().ToList();
            if (offending.Count > 0 || offendingTypes.Count > 0)
                throw new InvalidOperationException(
                    "Migration rules must not target the reserved history stream or the $-system namespace. "
                    + $"Offending stream target(s): {(offending.Count == 0 ? "(none)" : string.Join(", ", offending))}; "
                    + $"offending event-type target(s): {(offendingTypes.Count == 0 ? "(none)" : string.Join(", ", offendingTypes))}.");

            // m8b: migration ids are ordered ORDINALLY; mixed-width numeric prefixes silently misorder
            // (e.g. "10" sorts before "2"). Warn so the author zero-pads to equal width.
            var prefixWidths = defined.Select(d => LeadingDigitWidth(d.Id)).Where(w => w > 0).Distinct().ToList();
            if (prefixWidths.Count > 1)
                logger.LogWarning("Migration ids have INCONSISTENT numeric-prefix widths ({Widths}) — ordinal "
                    + "ordering may misorder them (e.g. \"10\" before \"2\"). Zero-pad ids to equal width.",
                    string.Join(", ", prefixWidths.OrderBy(w => w)));

            logger.LogInformation("{Count} pending migration(s): {Ids}", pending.Count,
                pending.Count == 0 ? "(none)" : string.Join(", ", pending.Select(p => p.Id)));

            // 4b. PROJECTION COPY (optional): pre-create the app's join projections on the EMPTY dest so they are
            //     the sole writers of the [OutputStream] merge streams, then PACE the copy below so they stay
            //     caught up event-by-event (no backlog → fromStreams can't type-cluster). Only $-system streams
            //     are added here, so the copy engine's dest-empty pre-flight still passes.
            var copier = projectionCopy is null ? null : new ProjectionCopier(_lf.CreateLogger<ProjectionCopier>());
            IReadOnlyList<ProjectionInfo> copied = [];
            IEventPacer? pacer = null;
            if (projectionCopy is not null)
            {
                Progress.EnterPhase(MigrationPhase.ProjectionCopy);
                await copier!.EnsureStandardProjectionsRunningAsync(projectionCopy.DestProjections, dryRun, ct)
                    .ConfigureAwait(false);
                copied = await copier.DiscoverAsync(projectionCopy.SourceProjections,
                    projectionCopy.SourceConnectionString, ct).ConfigureAwait(false);
                logger.LogInformation("Projection copy: {Count} user projection(s) to {Verb}: {Names}",
                    copied.Count, dryRun ? "create (dry run — not written)" : "pre-create",
                    copied.Count == 0 ? "(none)" : string.Join(", ", copied.Select(p => p.Name)));
                await copier.PreCreateAsync(copied, projectionCopy.DestProjections, dest, dryRun, ct)
                    .ConfigureAwait(false);
                if (!dryRun && copied.Count > 0)
                    pacer = copier.CreatePacer(copied, dest, projectionCopy.PerEventPaceTimeout);
            }

            // 5. Copy (or dry run) — reads source $all in commit order and writes dest in the same order. Without
            //    a projection copy the merge/link ($>) streams are SKIPPED and the app regenerates them on boot;
            //    with one, the pacer keeps the pre-created join projections caught up event-by-event.
            Progress.EnterPhase(MigrationPhase.EventCopy);
            if (!dryRun) Progress.MarkPendingRunning(runTimeUtc); // all pending applied together in one pass
            var copy = await new CopyEngine(_lf.CreateLogger<CopyEngine>())
                .RunAsync(source, dest, plan, runTimeUtc, dryRun, reserved, Progress, pacer, ct)
                .ConfigureAwait(false);

            // 5b. Final drain of the join projections to head (they're already caught up from pacing).
            if (projectionCopy is not null)
            {
                Progress.EnterPhase(MigrationPhase.Draining);
                await copier!.DrainAsync(copied, projectionCopy.DestProjections, dryRun,
                    projectionCopy.DrainTimeout, ct).ConfigureAwait(false);
            }

            if (copy.UnparseableVerbatim > 0)
                logger.LogWarning("{Count} source event(s) had unparseable JSON payloads and were copied "
                                  + "VERBATIM (byte-for-byte, not dropped) — no data loss.", copy.UnparseableVerbatim);

            // 5c. INDEX-BASED merge recovery (optional, the clean alternative to the projection copy): now that the
            //     dest holds every aggregate stream, create one user-defined index per app merge on the dest,
            //     each read in commit order via $idx-user-… (no paced projection, no type-clustering).
            IReadOnlyList<CreatedIndex> createdIndexes = [];
            if (indexCopy is not null)
            {
                Progress.EnterPhase(MigrationPhase.ProjectionCopy);
                createdIndexes = await new UserDefinedIndexMergeBuilder(_lf)
                    .BuildAsync(indexCopy, dest, dryRun, ct).ConfigureAwait(false);
                logger.LogInformation("Index copy: {Count} user-defined index(es) {Verb}: {Names}",
                    createdIndexes.Count, dryRun ? "planned (dry run — not created)" : "created on dest",
                    createdIndexes.Count == 0 ? "(none)" : string.Join(", ", createdIndexes.Select(i => i.IndexName)));
            }

            if (dryRun)
            {
                Progress.Complete(DateTime.UtcNow);
                return new MigrationRunResult
                {
                    DryRun = true,
                    PendingMigrationIds = pending.Select(p => p.Id).ToList(),
                    Copy = copy,
                    NewlyApplied = [],
                    CopiedProjections = copied.Select(p => p.Name).ToList(),
                    CreatedIndexes = createdIndexes
                };
            }

            // 6. Record history: existing (carried forward) + newly applied.
            Progress.EnterPhase(MigrationPhase.RecordingHistory);
            var newlyApplied = pending.Select(p => new MigrationApplied
            {
                Id = p.Id,
                Name = p.Name,
                Checksum = p.Checksum,
                AppliedAtUtc = runTimeUtc,
                SourceEvents = copy.SourceEvents,
                Kept = copy.Kept,
                Dropped = copy.MigrationStats[p.Id].Dropped,
                Transformed = copy.MigrationStats[p.Id].Transformed + copy.MigrationStats[p.Id].Renamed
            }).ToList();
            await history.WriteAsync(dest, applied, newlyApplied, runTimeUtc, ct).ConfigureAwait(false);
            Progress.MarkPendingCompleted(DateTime.UtcNow);

            // 7. Verify the destination against the copy bookkeeping.
            Progress.EnterPhase(MigrationPhase.Verifying);
            var verification = await new Verifier().VerifyAsync(dest, copy, reserved, ct).ConfigureAwait(false);
            logger.LogInformation("{Report}", verification.Format());

            Progress.Complete(DateTime.UtcNow);
            return new MigrationRunResult
            {
                DryRun = false,
                PendingMigrationIds = pending.Select(p => p.Id).ToList(),
                Copy = copy,
                Verification = verification,
                NewlyApplied = newlyApplied,
                CopiedProjections = copied.Select(p => p.Name).ToList(),
                CreatedIndexes = createdIndexes
            };
        }
        catch (Exception ex)
        {
            Progress.Fail(ex.Message, DateTime.UtcNow);
            throw;
        }
    }

    /// <summary>Count of the leading run of ASCII digits in an id (e.g. "0012_foo" → 4, "foo" → 0).</summary>
    private static int LeadingDigitWidth(string id)
    {
        var i = 0;
        while (i < id.Length && id[i] is >= '0' and <= '9') i++;
        return i;
    }
}
