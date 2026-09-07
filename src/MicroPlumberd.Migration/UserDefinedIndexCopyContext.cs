using System.Security.Cryptography;
using System.Text;
using KurrentDB.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MicroPlumberd.Migration;

/// <summary>
/// Enables the CLEAN index-based merge recovery (the alternative to <see cref="ProjectionCopyContext"/>). When
/// supplied to <see cref="MigrationRunner.RunAsync"/>, then — AFTER the plain copy has written every aggregate
/// stream to the fresh destination — the runner discovers the app's <c>[OutputStream]</c> join projections on
/// the source and creates one KurrentDB 26.1 USER-DEFINED INDEX per merge on the DESTINATION, filtering that
/// merge's event types. Each index is read via <see cref="UserDefinedIndexSource"/> in true commit order
/// (<c>$idx-user-{name}</c>), so a consumer gets the merged stream WITHOUT the type-clustering that forces
/// <see cref="ProjectionCopyContext"/> to pace its copy — no paced projection, no backlog, no <c>mp_query_hash</c>.
/// </summary>
/// <remarks>
/// <para><b>⚠ CONSUMER REWIRING REQUIRED — this is NOT a drop-in replacement for
/// <see cref="ProjectionCopyContext"/>.</b> The index path does NOT rebuild the physical <c>[OutputStream]</c>
/// merge streams: no <c>$&gt;</c> links are written into <c>FooModel_v1</c> and no <c>mp_query_hash</c> is
/// stamped. A consumer that still subscribes to the physical merge stream will see it EMPTY (or, worse, the app
/// will re-create its own <c>fromStreams</c> join projection on boot and type-cluster it again). To use this
/// path a consumer MUST be rewired to read the index stream named in <see cref="CreatedIndex.IndexStream"/>
/// (<c>$idx-user-…</c>) via a filtered <c>$all</c> read. If you need the physical merge stream rebuilt in place
/// for existing consumers, use <see cref="ProjectionCopyContext"/> instead.</para>
/// <para>This context and <see cref="ProjectionCopyContext"/> are mutually exclusive — supplying both to
/// <see cref="MigrationRunner.RunAsync"/> throws. Omitting both runs the simple copy (merge streams skipped).
/// The index path creates nothing on the destination BEFORE the copy, so the copy's dest-empty pre-flight is
/// unaffected.</para>
/// </remarks>
public sealed class UserDefinedIndexCopyContext
{
    /// <summary>Projection-management client for the SOURCE store (used to discover the app's join projections).</summary>
    public required KurrentDBProjectionManagementClient SourceProjections { get; init; }

    /// <summary>SOURCE connection string — used to read each projection's query verbatim over HTTP.</summary>
    public required string SourceConnectionString { get; init; }

    /// <summary>DEST connection string — the indexes are created and read on the destination (it holds the copy).</summary>
    public required string DestConnectionString { get; init; }

    /// <summary>Per-index readiness (create + backfill) timeout. Default 60s.</summary>
    public TimeSpan ReadyTimeout { get; init; } = TimeSpan.FromSeconds(60);
}

/// <summary>
/// A user-defined index created on the destination to serve one <c>[OutputStream]</c> merge in commit order.
/// <b>The physical merge stream <see cref="OutputStream"/> is NOT rebuilt</b> — consumers must be rewired to
/// read <see cref="IndexStream"/> instead (see <see cref="UserDefinedIndexCopyContext"/>).
/// </summary>
/// <param name="OutputStream">The app merge/output stream this index REPLACES (e.g. <c>FooModel_v1</c>) — left EMPTY on the dest.</param>
/// <param name="IndexName">The KurrentDB index name (normalised + hash-disambiguated).</param>
/// <param name="IndexStream">The read stream — <c>$idx-user-{IndexName}</c> — a consumer must read via filtered <c>$all</c> INSTEAD of <see cref="OutputStream"/>.</param>
/// <param name="EventTypes">The event types the index selects.</param>
public sealed record CreatedIndex(string OutputStream, string IndexName, string IndexStream,
    IReadOnlyCollection<string> EventTypes);

/// <summary>
/// Builds the destination user-defined indexes for <see cref="UserDefinedIndexCopyContext"/>: discovers the
/// app's join projections on the source (reusing <see cref="ProjectionCopier.DiscoverAsync"/>), then creates +
/// waits-ready one index per merge on the destination.
/// </summary>
internal sealed class UserDefinedIndexMergeBuilder(ILoggerFactory loggerFactory)
{
    private readonly ILoggerFactory _lf = loggerFactory ?? NullLoggerFactory.Instance;

    public async Task<IReadOnlyList<CreatedIndex>> BuildAsync(
        UserDefinedIndexCopyContext ctx, KurrentDBClient dest, bool dryRun, CancellationToken ct = default)
    {
        var logger = _lf.CreateLogger<UserDefinedIndexMergeBuilder>();

        // Reuse the ProjectionCopier discovery: it lists the source's user projections and parses each query's
        // OutputStream + $et link types — exactly the (name, event-types) an index needs.
        var copier = new ProjectionCopier(_lf.CreateLogger<ProjectionCopier>());
        var projections = await copier.DiscoverAsync(ctx.SourceProjections, ctx.SourceConnectionString, ct)
            .ConfigureAwait(false);

        var source = new UserDefinedIndexSource(dest, ctx.DestConnectionString,
            _lf.CreateLogger<UserDefinedIndexSource>());

        var created = new List<CreatedIndex>();
        foreach (var p in projections)
        {
            if (p.LinkTypes.Count == 0)
            {
                logger.LogInformation("Projection '{Name}' selects no $et types — no index created.", p.Name);
                continue;
            }

            var indexName = IndexNameFor(p.OutputStream);
            var spec = new UserDefinedIndexSpec { Name = indexName, EventTypes = p.LinkTypes };
            await source.EnsureAsync(spec, dryRun, ct).ConfigureAwait(false);
            if (!dryRun)
            {
                // AUTHORITATIVE readiness: the dest holds the whole copy and its $all is immediately consistent,
                // so the exact number of matching events is known — wait for the index to reach exactly that
                // (never a stability heuristic that could return a truncated merge).
                var expected = await source.CountMatchingAsync(p.LinkTypes, ct).ConfigureAwait(false);
                await source.WaitUntilReadyAsync(indexName, expected, ctx.ReadyTimeout, ct).ConfigureAwait(false);
            }

            var record = new CreatedIndex(p.OutputStream, indexName,
                UserDefinedIndexSource.IndexStream(indexName), p.LinkTypes.ToArray());
            created.Add(record);
            logger.LogInformation(
                "Merge '{Out}' → user-defined index '{Index}' (read '{Stream}') over types [{Types}].",
                p.OutputStream, indexName, record.IndexStream, string.Join(",", p.LinkTypes));
        }

        if (created.Count > 0)
            logger.LogWarning(
                "CONSUMER REWIRING REQUIRED: {Count} merge(s) are now served by user-defined indexes — the "
                + "physical [OutputStream] merge stream(s) [{Streams}] are NOT rebuilt on the dest. Consumers "
                + "must read the index stream(s) [{IndexStreams}] via a filtered $all read instead. Use "
                + "ProjectionCopyContext if you need the physical merge stream rebuilt in place.",
                created.Count, string.Join(", ", created.Select(c => c.OutputStream)),
                string.Join(", ", created.Select(c => c.IndexStream)));
        return created;
    }

    // A valid, collision-resistant index name for a merge/output stream: the normalised name plus a short SHA-256
    // suffix of the ORIGINAL stream id, so two different streams that normalise to the same string never clash.
    internal static string IndexNameFor(string outputStream)
    {
        var normalized = UserDefinedIndexSource.NormalizeName(outputStream);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(outputStream))).ToLowerInvariant();
        return $"mpidx-{normalized}-{hash[..8]}";
    }
}
