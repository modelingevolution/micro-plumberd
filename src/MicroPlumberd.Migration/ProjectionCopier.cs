using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using KurrentDB.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MicroPlumberd.Migration;

/// <summary>
/// A user (non-<c>$</c>) continuous projection discovered on the source. <see cref="Query"/> is the VERBATIM
/// query text; <see cref="OutputStream"/> and <see cref="LinkTypes"/> are parsed from it so the paced copy can
/// wait on the exact link emission.
/// </summary>
public sealed record ProjectionInfo(string Name, string Query, string OutputStream, IReadOnlySet<string> LinkTypes);

/// <summary>Callback the copy engine invokes after each kept event is appended, to PACE the copy.</summary>
internal interface IEventPacer
{
    /// <summary>Blocks until the join projection(s) have processed the just-appended event (its link emitted).</summary>
    Task AfterAppendedAsync(string destType, CancellationToken ct);
}

/// <summary>
/// Reinstates the owner's design: pre-create the app's <c>linkTo</c> JOIN projections on the EMPTY destination
/// so THEY are the sole writers of every <c>[OutputStream]</c> merge stream, then PACE the event copy so the
/// projection is kept caught up THROUGHOUT — it never accumulates a backlog, so a <c>fromStreams</c> catch-up
/// can never type-cluster (that clustering is a property of catching up over a multi-<c>$et</c> BACKLOG).
/// </summary>
/// <remarks>
/// <para>Pre-create: discover source user projections (skip <c>$</c>-system), read each query VERBATIM over the
/// HTTP projections API (the gRPC client exposes no query text), replicate MicroPlumberd's create flow
/// (Create→Disable→Update(emit)→Enable), and stamp <c>mp_query_hash</c> = SHA256(query) on the projection's
/// stream so the app's <c>TryCreateJoinProjection</c> NO-OPs on boot.</para>
/// <para>Pace: the copy engine flushes ONE event at a time and calls <see cref="IEventPacer.AfterAppendedAsync"/>;
/// the pacer waits until the merge stream's link count advances for that event before the next is written, so
/// events are processed singly, in commit order.</para>
/// </remarks>
internal sealed class ProjectionCopier(ILogger? logger = null)
{
    private const string QueryHashMetadataKey = "mp_query_hash"; // MUST match KurrentDBProjectionManagementClientExtensions

    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    /// <summary>Lists the source's user (non-<c>$</c>) continuous projections, reading each query VERBATIM over HTTP.</summary>
    public async Task<IReadOnlyList<ProjectionInfo>> DiscoverAsync(
        KurrentDBProjectionManagementClient sourceProjections,
        string sourceConnectionString,
        CancellationToken ct = default)
    {
        var (baseUri, user, pass) = ParseHttpEndpoint(sourceConnectionString);
        using var http = CreateHttpClient(user, pass);

        var result = new List<ProjectionInfo>();
        await foreach (var p in sourceProjections.ListContinuousAsync().WithCancellation(ct).ConfigureAwait(false))
        {
            if (string.IsNullOrEmpty(p.Name) || p.Name[0] == '$') continue;
            var query = await ReadQueryVerbatimAsync(http, baseUri, p.Name, ct).ConfigureAwait(false);
            var linkTypes = ParseLinkTypes(query);
            var outputStream = ParseOutputStream(query) ?? p.Name;
            result.Add(new ProjectionInfo(p.Name, query, outputStream, linkTypes));
            _logger.LogInformation("Discovered user projection '{Name}' → output '{Out}', link types [{Types}].",
                p.Name, outputStream, string.Join(",", linkTypes));
        }
        return result;
    }

    /// <summary>Verifies the destination's standard <c>$by_event_type</c> projection is running (required to feed the joins).</summary>
    public async Task EnsureStandardProjectionsRunningAsync(
        KurrentDBProjectionManagementClient destProjections, bool dryRun, CancellationToken ct = default)
    {
        const string sys = "$by_event_type";
        ProjectionDetails? status;
        try { status = await destProjections.GetStatusAsync(sys, cancellationToken: ct).ConfigureAwait(false); }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Destination must run standard projections ('{sys}'). Start the dest EventStore with "
                + "KURRENTDB_RUN_PROJECTIONS=All and KURRENTDB_START_STANDARD_PROJECTIONS=true.", ex);
        }

        if (IsRunning(status)) { _logger.LogInformation("Standard projection '{Sys}' is running.", sys); return; }
        if (dryRun) { _logger.LogWarning("DRY RUN: '{Sys}' is '{Status}', not running.", sys, status?.Status); return; }

        await destProjections.EnableAsync(sys, cancellationToken: ct).ConfigureAwait(false);
        status = await destProjections.GetStatusAsync(sys, cancellationToken: ct).ConfigureAwait(false);
        if (!IsRunning(status))
            throw new InvalidOperationException(
                $"Destination standard projection '{sys}' is '{status?.Status}' and could not be started.");
    }

    /// <summary>Pre-creates each discovered projection on the (empty) dest as a live sole writer + stamps mp_query_hash.</summary>
    public async Task PreCreateAsync(
        IReadOnlyList<ProjectionInfo> projections,
        KurrentDBProjectionManagementClient destProjections,
        KurrentDBClient destClient,
        bool dryRun,
        CancellationToken ct = default)
    {
        foreach (var p in projections)
        {
            if (dryRun)
            {
                _logger.LogInformation("DRY RUN: would create projection '{Name}' and stamp mp_query_hash.", p.Name);
                continue;
            }
            await destProjections.CreateContinuousAsync(p.Name, p.Query, trackEmittedStreams: false,
                cancellationToken: ct).ConfigureAwait(false);
            await destProjections.DisableAsync(p.Name, cancellationToken: ct).ConfigureAwait(false);
            await destProjections.UpdateAsync(p.Name, p.Query, emitEnabled: true, cancellationToken: ct)
                .ConfigureAwait(false);
            await destProjections.EnableAsync(p.Name, cancellationToken: ct).ConfigureAwait(false);
            await StoreQueryHashAsync(destClient, p.Name, ComputeQueryHash(p.Query), ct).ConfigureAwait(false);
            _logger.LogInformation("Pre-created live projection '{Name}' (emit ON) + mp_query_hash.", p.Name);
        }
    }

    /// <summary>Builds the pacer that keeps the projections caught up event-by-event during the copy.</summary>
    public IEventPacer CreatePacer(IReadOnlyList<ProjectionInfo> projections, KurrentDBClient destClient,
        TimeSpan perEventTimeout) =>
        new ProjectionPacer(projections, destClient, perEventTimeout, _logger);

    /// <summary>Final drain: poll each projection to head so its checkpoint sits at the log head before boot.</summary>
    public async Task DrainAsync(IReadOnlyList<ProjectionInfo> projections,
        KurrentDBProjectionManagementClient destProjections, bool dryRun, TimeSpan timeout,
        CancellationToken ct = default)
    {
        if (dryRun) return;
        foreach (var p in projections)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                var s = await destProjections.GetStatusAsync(p.Name, cancellationToken: ct).ConfigureAwait(false);
                if (IsDrained(s)) break;
                await Task.Delay(100, ct).ConfigureAwait(false);
            }
        }
    }

    // ---- the pacer -----------------------------------------------------------------------------------------

    private sealed class ProjectionPacer : IEventPacer
    {
        private readonly KurrentDBClient _dest;
        private readonly TimeSpan _timeout;
        private readonly ILogger _logger;
        // Per projection: name, its output (merge) stream, its linkable dest-type set, and how many links waited-for.
        private readonly List<(string Name, string Output, IReadOnlySet<string> Types, long[] Expected)> _projections;

        public ProjectionPacer(IReadOnlyList<ProjectionInfo> projections, KurrentDBClient dest, TimeSpan timeout,
            ILogger logger)
        {
            _dest = dest;
            _timeout = timeout;
            _logger = logger;
            _projections = projections.Select(p => (p.Name, p.OutputStream, p.LinkTypes, new long[1])).ToList();
        }

        public async Task AfterAppendedAsync(string destType, CancellationToken ct)
        {
            foreach (var (name, output, types, expected) in _projections)
            {
                if (!types.Contains(destType)) continue; // this event is not linked into this merge stream
                expected[0]++;
                await WaitForLinkCountAsync(name, output, destType, expected[0], ct).ConfigureAwait(false);
            }
        }

        // Blocks until the merge stream has at least <paramref name="expected"/> links (its last revision + 1),
        // i.e. the projection has emitted the link for the just-appended event → it is caught up, no backlog.
        // BOUNDED: if the projection does not emit within the timeout it FAILS LOUD (aborting the run) rather
        // than hanging — a stalled/faulted projection mid-migration must not deadlock the copy.
        private async Task WaitForLinkCountAsync(string projection, string outputStream, string destType,
            long expected, CancellationToken ct)
        {
            var deadline = DateTime.UtcNow + _timeout;
            while (true)
            {
                if (await LinkCountAsync(outputStream, ct).ConfigureAwait(false) >= expected) return;
                if (DateTime.UtcNow >= deadline)
                    throw new TimeoutException(
                        $"Paced copy STALLED: projection '{projection}' did not emit link #{expected} into merge "
                        + $"stream '{outputStream}' for the just-appended '{destType}' event within "
                        + $"{_timeout.TotalSeconds:0}s. The projection is not keeping up (stopped/faulted?) — "
                        + "aborting the migration rather than proceeding with a possibly-reordered merge stream.");
                await Task.Delay(15, ct).ConfigureAwait(false);
            }
        }

        // O(1): the stream's last event number + 1 (0 if it doesn't exist yet).
        private async Task<long> LinkCountAsync(string stream, CancellationToken ct)
        {
            var read = _dest.ReadStreamAsync(Direction.Backwards, stream, StreamPosition.End, maxCount: 1,
                resolveLinkTos: false, cancellationToken: ct);
            if (await read.ReadState.ConfigureAwait(false) == ReadState.StreamNotFound) return 0;
            await foreach (var re in read.ConfigureAwait(false))
                return (long)re.OriginalEventNumber.ToUInt64() + 1;
            return 0;
        }
    }

    // ---- helpers -------------------------------------------------------------------------------------------

    private static bool IsRunning(ProjectionDetails? s) =>
        s?.Status is not null && s.Status.Contains("Running", StringComparison.OrdinalIgnoreCase);

    private static bool IsDrained(ProjectionDetails? s) =>
        s is not null && IsRunning(s) && s.Progress >= 99.9999f && s.BufferedEvents == 0
        && s.WritePendingEventsBeforeCheckpoint == 0 && s.WritePendingEventsAfterCheckpoint == 0;

    // Parse the linkable event types from a fromStreams(['$et-A','$et-B']) projection query.
    internal static IReadOnlySet<string> ParseLinkTypes(string query)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(query, @"'\$et-([^']+)'"))
            set.Add(m.Groups[1].Value);
        return set;
    }

    // Parse the linkTo('X', …) output stream target from the query (null if absent → caller falls back to name).
    internal static string? ParseOutputStream(string query)
    {
        var m = Regex.Match(query, @"linkTo\(\s*'([^']+)'");
        return m.Success ? m.Groups[1].Value : null;
    }

    private static string ComputeQueryHash(string query) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(query)));

    private static async Task StoreQueryHashAsync(KurrentDBClient esClient, string stream, string hash,
        CancellationToken ct)
    {
        var existing = await esClient.GetStreamMetadataAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        var customDoc = JsonDocument.Parse($"{{\"{QueryHashMetadataKey}\":\"{hash}\"}}");
        var newMeta = new StreamMetadata(existing.Metadata.MaxCount, existing.Metadata.MaxAge,
            existing.Metadata.TruncateBefore, existing.Metadata.CacheControl, existing.Metadata.Acl, customDoc);
        await esClient.SetStreamMetadataAsync(stream, StreamState.Any, newMeta, cancellationToken: ct)
            .ConfigureAwait(false);
    }

    private static async Task<string> ReadQueryVerbatimAsync(HttpClient http, Uri baseUri, string name,
        CancellationToken ct)
    {
        var url = new Uri(baseUri, $"projection/{Uri.EscapeDataString(name)}/query");
        using var resp = await http.GetAsync(url, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    private static HttpClient CreateHttpClient(string user, string pass)
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        var http = new HttpClient(handler);
        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{user}:{pass}"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
        return http;
    }

    /// <summary>Derives the HTTP(S) base + basic-auth creds from a KurrentDB connection string.</summary>
    internal static (Uri BaseUri, string User, string Pass) ParseHttpEndpoint(string connectionString)
    {
        var m = Regex.Match(connectionString,
            @"^(?<scheme>[a-zA-Z0-9+]+)://(?:(?<user>[^:@/]+):(?<pass>[^@/]+)@)?(?<hosts>[^/?]+)(?:/[^?]*)?(?:\?(?<query>.*))?$");
        if (!m.Success)
            throw new ArgumentException($"Unrecognised KurrentDB connection string: '{connectionString}'.");

        var user = m.Groups["user"].Success ? m.Groups["user"].Value : "admin";
        var pass = m.Groups["pass"].Success ? m.Groups["pass"].Value : "changeit";
        var firstHost = m.Groups["hosts"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
        if (!firstHost.Contains(':')) firstHost += ":2113";

        var tls = true;
        if (m.Groups["query"].Success)
            foreach (var kv in m.Groups["query"].Value.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = kv.Split('=', 2);
                if (parts.Length == 2 && parts[0].Equals("tls", StringComparison.OrdinalIgnoreCase))
                    tls = !parts[1].Equals("false", StringComparison.OrdinalIgnoreCase);
            }

        return (new Uri($"{(tls ? "https" : "http")}://{firstHost}/"), user, pass);
    }
}
