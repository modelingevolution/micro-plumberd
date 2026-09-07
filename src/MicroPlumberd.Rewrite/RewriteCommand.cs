using System.Diagnostics;
using Docker.DotNet;
using KurrentDB.Client;
using MicroPlumberd.Migration;
using MicroPlumberd.Migration.Scripting;
using Microsoft.Extensions.Logging;

namespace MicroPlumberd.Rewrite;

/// <summary>
/// The tool itself. <see cref="Program"/> only parses arguments into <see cref="RewriteOptions"/> and calls
/// this, so the end-to-end suite drives exactly the code a real invocation runs.
/// </summary>
public sealed class RewriteCommand
{
    /// <summary>How long the scratch store gets to come up.</summary>
    private static readonly TimeSpan ScratchStartTimeout = TimeSpan.FromSeconds(120);

    /// <summary>How long the original container gets to come back after the swap.</summary>
    private static readonly TimeSpan RestartTimeout = TimeSpan.FromSeconds(120);

    /// <summary>The standard projection the copy engine needs live on the destination.</summary>
    private const string ByEventType = "$by_event_type";

    /// <summary>Runs the tool. Never throws for an expected failure — the outcome is in the report's code.</summary>
    public static async Task<RewriteReport> RunAsync(RewriteOptions options, IDockerClient docker,
        ILoggerFactory loggerFactory, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(docker);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        var logger = loggerFactory.CreateLogger<RewriteCommand>();
        var sw = Stopwatch.StartNew();

        // Refused before ANY docker call, in every mode: the answer does not depend on inspecting anything,
        // and an operator must not watch the tool start work it will not finish.
        if (options.ForceVolumeCopy)
        {
            var message = "named-volume rewrite is not implemented in this version; move the store to a bind "
                          + "mount or wait for a version that implements --force-volume-copy";
            logger.LogError("{Message}", message);
            return new RewriteReport { Code = ExitCode.ScriptError, Headline = message, Elapsed = sw.Elapsed };
        }

        try
        {
            return options.Mode switch
            {
                RewriteMode.Status => await StatusAsync(options, docker, logger, sw, ct).ConfigureAwait(false),
                RewriteMode.Rollback => await RollbackAsync(options, docker, loggerFactory, logger, sw, ct)
                    .ConfigureAwait(false),
                _ => await RewriteAsync(options, docker, loggerFactory, logger, sw, ct).ConfigureAwait(false)
            };
        }
        catch (RewriteRefusedException ex)
        {
            logger.LogError("{Message}", ex.Message);
            return new RewriteReport { Code = ex.Code, Headline = ex.Message, Elapsed = sw.Elapsed };
        }
    }

    // ================================================================= rewrite

    private static async Task<RewriteReport> RewriteAsync(RewriteOptions options, IDockerClient docker,
        ILoggerFactory lf, ILogger logger, Stopwatch sw, CancellationToken ct)
    {
        // STEP 0 — the script is parsed BEFORE anything else. A typo must cost the operator nothing: no
        // container started, no store touched, no directory created.
        ScriptMigration? migration;
        string? source;
        try
        {
            source = options.ReadScriptSource();
            migration = source is null ? null : new ScriptMigration(source, ScriptIdPrefix(DateTime.UtcNow), logger);
        }
        catch (Exception ex) when (ex is ScriptSyntaxException or FileNotFoundException or ArgumentException)
        {
            return new RewriteReport { Code = ExitCode.ScriptError, Headline = ex.Message, Elapsed = sw.Elapsed };
        }

        var store = new DockerStore(docker, logger);

        // STEP 1 — inspect.
        var c = await store.InspectAsync(options.Container, ct).ConfigureAwait(false);
        RequireSwappableData(c, options);
        var connectionString = DockerStore.ConnectionString(c);

        // STEP 2 — guards, BEFORE the tool opens its own client to the old store, so the connection count it
        // reads is the operator's, never its own.
        var guards = await EvaluateGuardsAsync(store, c, connectionString, ct).ConfigureAwait(false);
        if (guards.Refusal is not null)
            return new RewriteReport
            {
                Code = ExitCode.GuardRefusal, Headline = guards.Refusal, Container = c,
                Notes = guards.Notes, Elapsed = sw.Elapsed
            };

        // The plan, then the operator's confirmation.
        var stamp = DirectorySwap.Stamp(DateTime.UtcNow);
        var descriptors = DescriptorsOf(migration);
        if (!options.Yes && !await ConfirmAsync(options, c, migration, descriptors, ct).ConfigureAwait(false))
            return new RewriteReport
            {
                Code = ExitCode.GuardRefusal, Headline = "Cancelled at the confirmation prompt; nothing changed.",
                Container = c, Elapsed = sw.Elapsed
            };

        await store.EnsureImageAsync(c.Image, ct).ConfigureAwait(false);

        var swap = new DirectorySwap(c.Data, logger);
        var newStoreDir = swap.CreateNewStoreDir(stamp);
        var scratchName = $"mp-rewrite-{c.Name}-{stamp}";
        ScratchStore? scratch = null;
        SwapResult? swapped = null;

        try
        {
            // STEP 3 — the scratch store. A dry run starts one too: the engine's destination pre-checks need
            // it, and running the two modes down one code path is what makes a dry run evidence about the
            // real one.
            scratch = await store.StartScratchAsync(c, newStoreDir, scratchName, ct).ConfigureAwait(false);
            await StoreHealth.WaitLiveAsync(scratch.ConnectionString, ScratchStartTimeout, logger, ct)
                .ConfigureAwait(false);

            await using var destClient = new KurrentDBClient(KurrentDBClientSettings.Create(scratch.ConnectionString));
            await using var destProjections =
                new KurrentDBProjectionManagementClient(KurrentDBClientSettings.Create(scratch.ConnectionString));
            await StoreHealth.WaitProjectionRunningAsync(destProjections, ByEventType, ScratchStartTimeout, logger, ct)
                .ConfigureAwait(false);

            await using var sourceClient = new KurrentDBClient(KurrentDBClientSettings.Create(connectionString));
            await using var sourceProjections =
                new KurrentDBProjectionManagementClient(KurrentDBClientSettings.Create(connectionString));

            var projectionCopy = options.NoProjectionCopy
                ? null
                : new ProjectionCopyContext
                {
                    SourceProjections = sourceProjections,
                    DestProjections = destProjections,
                    SourceConnectionString = connectionString
                };

            // STEP 4/5 — copy and verify. Anything that fails here leaves the OLD store untouched.
            MigrationRunResult run;
            try
            {
                run = await new MigrationRunner(lf).RunAsync(sourceClient, destClient,
                    migration is null ? [] : [migration], options.DryRun, projectionCopy, null, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "The copy failed; the old store was not touched.");
                return new RewriteReport
                {
                    Code = ExitCode.EngineFailure, Container = c, DryRun = options.DryRun,
                    Headline = $"Copy failed, the old store is untouched: {ex.Message}", Elapsed = sw.Elapsed
                };
            }

            if (run.Verification is { AllOk: false })
                return Fill(new RewriteReport
                {
                    Code = ExitCode.EngineFailure, Container = c,
                    Headline = "Verification MISMATCH — the swap was not performed and the old store is untouched.",
                    Verification = run.Verification, Elapsed = sw.Elapsed
                }, run, migration, descriptors);

            if (options.DryRun)
                return Fill(new RewriteReport
                {
                    Code = ExitCode.Ok, Container = c, DryRun = true,
                    Headline = $"Dry run complete — {run.Copy.SourceEvents} event(s) read, "
                               + $"{run.Copy.Kept} would be written, {run.Copy.Dropped} dropped. Nothing changed.",
                    Elapsed = sw.Elapsed
                }, run, migration, descriptors);

            // STEP 6 — the swap. Both stores must be stopped first, and the scratch container must be gone
            // before its data directory is renamed out from under it.
            await store.StopAsync(scratch.Id, ct).ConfigureAwait(false);
            await store.RemoveAsync(scratch.Id, ct).ConfigureAwait(false);
            scratch = null;

            await store.StopAsync(c.Id, ct).ConfigureAwait(false);

            try
            {
                swapped = swap.Swap(newStoreDir, stamp);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await store.StartAsync(c.Id, ct).ConfigureAwait(false);
                return new RewriteReport
                {
                    Code = ExitCode.FilesystemFailure, Container = c,
                    Headline = $"The swap failed and the original data is in place: {ex.Message}",
                    Elapsed = sw.Elapsed
                };
            }

            if (options.FailAfterSwapForTest)
                throw new InvalidOperationException(
                    "MP_REWRITE_TEST_FAIL_AFTER_SWAP: injected failure immediately after the swap.");

            await store.StartAsync(c.Id, ct).ConfigureAwait(false);
            await StoreHealth.WaitLiveAsync(connectionString, RestartTimeout, logger, ct).ConfigureAwait(false);

            await using var afterProjections =
                new KurrentDBProjectionManagementClient(KurrentDBClientSettings.Create(connectionString));
            var faulted = await StoreHealth.FaultedProjectionsAsync(afterProjections, ct).ConfigureAwait(false);
            if (faulted.Count > 0)
                throw new InvalidOperationException(
                    $"After the swap these projections are Faulted: {string.Join(", ", faulted)}.");

            return Fill(new RewriteReport
            {
                Code = ExitCode.Ok, Container = c,
                Headline = $"Rewrite complete — {c.Name} is running on the new store.",
                BackupDir = swapped.BackupDir, Verification = run.Verification, Elapsed = sw.Elapsed
            }, run, migration, descriptors);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not RewriteRefusedException)
        {
            // STEP 6b — anything after the swap puts the original data back and starts the container on it.
            if (swapped is not null)
            {
                await SafeStopAsync(store, c.Id, logger, ct).ConfigureAwait(false);
                swap.Restore(swapped);
                await PurgeNewStoreDirAsync(store, c.Image, swapped.NewStoreDir, logger, ct).ConfigureAwait(false);
                await store.StartAsync(c.Id, ct).ConfigureAwait(false);
                await SafeWaitLiveAsync(connectionString, logger, ct).ConfigureAwait(false);
                return new RewriteReport
                {
                    Code = ExitCode.FilesystemFailure, Container = c,
                    Headline = $"Failed after the swap; the ORIGINAL store has been restored and {c.Name} is "
                               + $"running on it: {ex.Message}",
                    Elapsed = sw.Elapsed
                };
            }
            logger.LogError(ex, "The rewrite failed before the swap.");
            return new RewriteReport
            {
                Code = ExitCode.EngineFailure, Container = c, DryRun = options.DryRun,
                Headline = $"Failed before the swap, the old store is untouched: {ex.Message}", Elapsed = sw.Elapsed
            };
        }
        finally
        {
            // The scratch container and its directory never outlive the run — a dry run included (E2E-06).
            if (scratch is not null) await store.RemoveAsync(scratch.Id, ct).ConfigureAwait(false);
            if (swapped is null)
                await PurgeNewStoreDirAsync(store, c.Image, newStoreDir, logger, ct).ConfigureAwait(false);
        }
    }

    // ================================================================= status / rollback

    private static async Task<RewriteReport> StatusAsync(RewriteOptions options, IDockerClient docker,
        ILogger logger, Stopwatch sw, CancellationToken ct)
    {
        var store = new DockerStore(docker, logger);
        var c = await store.InspectAsync(options.Container, ct).ConfigureAwait(false);

        var notes = new List<string>();
        string? safe;
        if (c.Data.IsBind)
        {
            var swap = new DirectorySwap(c.Data, logger);
            var backups = swap.Backups();
            notes.Add($"backups      : {(backups.Count == 0 ? "(none)" : string.Join(", ", backups))}");
            var guards = await EvaluateGuardsAsync(store, c, DockerStore.ConnectionString(c), ct)
                .ConfigureAwait(false);
            notes.AddRange(guards.Notes);
            safe = guards.Refusal is null ? "yes" : $"no — {guards.Refusal}";
        }
        else
        {
            notes.Add($"backups      : (not applicable — named volume '{c.Data.VolumeName}')");
            safe = "no — the store is on a named volume; --force-volume-copy is required";
        }
        notes.Add($"safe         : {safe}");

        return new RewriteReport
        {
            Code = ExitCode.Ok, Container = c, Notes = notes, Elapsed = sw.Elapsed,
            Headline = $"Status of {c.Name}:"
        };
    }

    private static async Task<RewriteReport> RollbackAsync(RewriteOptions options, IDockerClient docker,
        ILoggerFactory lf, ILogger logger, Stopwatch sw, CancellationToken ct)
    {
        var store = new DockerStore(docker, logger);
        var c = await store.InspectAsync(options.Container, ct).ConfigureAwait(false);
        RequireSwappableData(c, options);

        var swap = new DirectorySwap(c.Data, logger);
        var stamp = DirectorySwap.Stamp(DateTime.UtcNow);

        var wasRunning = await store.IsRunningAsync(c.Id, ct).ConfigureAwait(false);
        if (wasRunning) await store.StopAsync(c.Id, ct).ConfigureAwait(false);

        RollbackResult result;
        try
        {
            result = swap.Rollback(options.BackupDir, stamp);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await store.StartAsync(c.Id, ct).ConfigureAwait(false);
            return new RewriteReport
            {
                Code = ExitCode.FilesystemFailure, Container = c, Elapsed = sw.Elapsed,
                Headline = $"Rollback failed and the store was left as it was: {ex.Message}"
            };
        }

        await store.StartAsync(c.Id, ct).ConfigureAwait(false);
        await StoreHealth.WaitLiveAsync(DockerStore.ConnectionString(c), RestartTimeout, logger, ct)
            .ConfigureAwait(false);

        return new RewriteReport
        {
            Code = ExitCode.Ok, Container = c, Elapsed = sw.Elapsed, BackupDir = result.RestoredFrom,
            Headline = $"Rolled back — {c.Name} is running on {result.RestoredFrom}.",
            Notes = [$"rewritten store kept at: {result.DisplacedTo}"]
        };
    }

    // ================================================================= helpers

    /// <summary>The id prefix the tool gives every run, so a second pure copy is its own history record.</summary>
    public static string ScriptIdPrefix(DateTime utc) => $"rewrite_{utc:yyyyMMddTHHmmss}";

    private static void RequireSwappableData(StoreContainer c, RewriteOptions options)
    {
        if (c.Data.IsBind) return;
        throw new RewriteRefusedException(ExitCode.GuardRefusal,
            $"The store of '{c.Name}' is on the named volume '{c.Data.VolumeName}', which this tool cannot "
            + "swap by renaming directories. Move the store onto a bind mount.");
    }

    private sealed record GuardOutcome(string? Refusal, IReadOnlyList<string> Notes);

    /// <summary>
    /// The two refusals with no override (ADR 7): a sibling container of the same compose project is running,
    /// or a client has work in flight against the store.
    /// </summary>
    private static async Task<GuardOutcome> EvaluateGuardsAsync(DockerStore store, StoreContainer c,
        string connectionString, CancellationToken ct)
    {
        var notes = new List<string>();

        var siblings = await store.FindRunningSiblingsAsync(c, ct).ConfigureAwait(false);
        notes.Add($"siblings     : {(siblings.Count == 0 ? "none running" : string.Join(", ", siblings))}");

        var (grpc, kestrel) = await DockerStore.ReadConnectionMetricsAsync(connectionString, ct)
            .ConfigureAwait(false);
        notes.Add($"clients      : {DockerStore.OpenGrpcCallsMetric}={grpc} "
                  + $"({DockerStore.KestrelConnectionsMetric}={kestrel}, includes this tool's own scrape)");

        if (siblings.Count > 0)
            return new GuardOutcome(
                $"Refusing: {siblings.Count} container(s) of compose project '{c.ComposeProject}' are still "
                + $"running ({string.Join(", ", siblings)}). Stop them first — a writer mid-rewrite loses data.",
                notes);

        if (grpc < 0)
            return new GuardOutcome(
                $"Refusing: the store does not expose '{DockerStore.OpenGrpcCallsMetric}', so the "
                + "connected-client guard cannot be evaluated. Refusing beats rewriting under a live writer.",
                notes);

        if (grpc > 0)
            return new GuardOutcome(
                $"Refusing: {grpc} gRPC call(s) are open against the store ({DockerStore.OpenGrpcCallsMetric}"
                + $"={grpc}). Disconnect every client first — a writer mid-rewrite loses data.",
                notes);

        return new GuardOutcome(null, notes);
    }

    private static IReadOnlyList<string> DescriptorsOf(ScriptMigration? migration)
    {
        if (migration is null) return [];
        var b = new DescriptorRecorder();
        migration.Migrate(b);
        return b.Descriptors;
    }

    private static async Task<bool> ConfirmAsync(RewriteOptions options, StoreContainer c,
        ScriptMigration? migration, IReadOnlyList<string> descriptors, CancellationToken ct)
    {
        var output = options.Output ?? Console.Out;
        await output.WriteLineAsync($"About to rewrite the store of container '{c.Name}' ({c.Image}).")
            .ConfigureAwait(false);
        await output.WriteLineAsync($"  data          : {c.Data.StoreDir}").ConfigureAwait(false);
        await output.WriteLineAsync($"  script sha256 : {migration?.ScriptChecksum ?? "(none — pure copy)"}")
            .ConfigureAwait(false);
        foreach (var d in descriptors) await output.WriteLineAsync($"  rule          : {d}").ConfigureAwait(false);
        await output.WriteLineAsync("Type 'yes' to continue: ").ConfigureAwait(false);

        var input = options.ConfirmationInput ?? Console.In;
        var answer = await input.ReadLineAsync(ct).ConfigureAwait(false);
        return string.Equals(answer?.Trim(), "yes", StringComparison.OrdinalIgnoreCase);
    }

    private static RewriteReport Fill(RewriteReport r, MigrationRunResult run, ScriptMigration? migration,
        IReadOnlyList<string> descriptors) => r with
    {
        MigrationId = migration?.Id,
        ScriptChecksum = migration?.ScriptChecksum,
        Descriptors = descriptors,
        SourceEvents = run.Copy.SourceEvents,
        Kept = run.Copy.Kept,
        Dropped = run.Copy.Dropped,
        UnparseableVerbatim = run.Copy.UnparseableVerbatim,
        CopiedProjections = run.CopiedProjections,
        DroppedStreams = run.Copy.SourceStreams
            .Where(kv => kv.Value.Kept == 0 && kv.Value.Dropped > 0)
            .Select(kv => (kv.Key, kv.Value.Dropped)).ToList(),
        AffectedStreams = run.Copy.SourceStreams
            .Select(kv => (kv.Key, kv.Value.TargetStream, kv.Value.SourceCount, kv.Value.Kept)).ToList()
    };

    /// <summary>
    /// Removes a <c>.new.</c> directory. The suffix check is the safety interlock on a root-container
    /// <c>rm -rf</c>: this tool must only ever delete a directory IT created for this run.
    /// </summary>
    private static async Task PurgeNewStoreDirAsync(DockerStore store, string image, string dir, ILogger logger,
        CancellationToken ct)
    {
        if (!Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar)).Contains(DirectorySwap.NewSuffix,
                StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Refusing to purge '{dir}': only a '{DirectorySwap.NewSuffix}' directory this run created "
                + "may be removed.");
        try
        {
            await store.PurgeDirectoryAsync(image, dir, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Leftover scratch data is untidy, never dangerous — it must not mask the real outcome of the run.
            logger.LogWarning(ex, "Could not remove the scratch directory {Dir}; remove it by hand.", dir);
        }
    }

    private static async Task SafeStopAsync(DockerStore store, string id, ILogger logger, CancellationToken ct)
    {
        try { if (await store.IsRunningAsync(id, ct).ConfigureAwait(false)) await store.StopAsync(id, ct).ConfigureAwait(false); }
        catch (Exception ex) { logger.LogWarning(ex, "Could not stop {Id} while restoring.", id); }
    }

    private static async Task SafeWaitLiveAsync(string connectionString, ILogger logger, CancellationToken ct)
    {
        try { await StoreHealth.WaitLiveAsync(connectionString, RestartTimeout, logger, ct).ConfigureAwait(false); }
        catch (Exception ex) { logger.LogWarning(ex, "The restored store did not report live in time."); }
    }
}
