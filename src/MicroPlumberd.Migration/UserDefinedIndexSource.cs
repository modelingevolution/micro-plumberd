using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Grpc.Core;
using KurrentDB.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MicroPlumberd.Migration;

/// <summary>
/// Selects which stored events a <see cref="UserDefinedIndexSource"/> indexes. <see cref="Name"/> is the
/// KurrentDB index name (lower-case alphanumerics, <c>_</c> and <c>-</c> only — see
/// <see cref="UserDefinedIndexSource.NormalizeName"/>); <see cref="EventTypes"/> are the stored event-type
/// (schema) names to include. An EMPTY <see cref="EventTypes"/> indexes ALL records.
/// </summary>
public sealed record UserDefinedIndexSpec
{
    /// <summary>The index name. Combined with the fixed prefix it forms the read stream <c>$idx-user-{Name}</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Stored event-type names to include; empty ⇒ index every record.</summary>
    public required IReadOnlySet<string> EventTypes { get; init; }
}

/// <summary>
/// The CLEAN merged read-source for a migration (the alternative to <see cref="ProjectionCopier"/>). It creates
/// a KurrentDB 26.1 USER-DEFINED INDEX over a set of event types and streams the matching events back in true
/// <c>$all</c> COMMIT order via the filtered-<c>$all</c> read (<c>ReadAllAsync</c> +
/// <see cref="StreamFilter.Prefix(string)"/> over <c>$idx-user-{name}</c> + <c>resolveLinkTos</c>).
/// </summary>
/// <remarks>
/// <para><b>Why this replaces the paced projection copy.</b> An app-style
/// <c>fromStreams(['$et-A','$et-B']).linkTo(X)</c> join projection TYPE-CLUSTERS on catch-up (it drains
/// <c>$et-A</c> then <c>$et-B</c> → 0,2,4,1,3), which is why <see cref="ProjectionCopier"/> has to PACE the copy
/// event-by-event so a backlog never forms. A user-defined index is read through the filtered <c>$all</c> API,
/// which yields events in native commit order (0,1,2,3,4) with NO pacing and NO backlog games — exactly what a
/// migration merge needs.</para>
/// <para><b>Creation is idempotent.</b> <see cref="EnsureAsync"/> POSTs <c>/v2/indexes/{name}</c>; an existing
/// index (HTTP 409 <c>INDEX_ALREADY_EXISTS</c>, or an identical 200) is a no-op. If the existing index has a
/// DIFFERENT filter than requested it is left as-is and a warning is logged (KurrentDB does not redefine an
/// index in place).</para>
/// <para><b>Readiness (the NotFound/backfill gotcha).</b> The index reports <c>INDEX_STATE_STARTED</c>
/// IMMEDIATELY after creation while it is still BACKFILLING history, and its resource may briefly 404 right
/// after the POST. <see cref="WaitUntilReadyAsync"/> therefore treats a 404 as "still building" (not "absent")
/// and, once the resource is STARTED, waits until the filtered read COUNT stops growing across consecutive
/// polls — i.e. the index has caught up to the (frozen, offline) source head — before the read is trusted to be
/// complete. Bounded by a timeout: a stalled build FAILS LOUD rather than yielding a short merge.</para>
/// <para>Every seam logs (index name + the failing URI on error) using the injected <see cref="ILogger"/>,
/// defaulting to <see cref="NullLogger"/> so the type is safe to new up anywhere (incl. WASM).</para>
/// </remarks>
public sealed class UserDefinedIndexSource
{
    /// <summary>The read stream for an index is <c>$idx-user-{name}</c> — read it via filtered <c>$all</c>.</summary>
    public const string IndexStreamPrefixRoot = "$idx-user-";

    private readonly KurrentDBClient _client;
    private readonly Uri _httpBase;
    private readonly string _user;
    private readonly string _pass;
    private readonly ILogger _logger;

    /// <summary>
    /// </summary>
    /// <param name="client">gRPC client used for the filtered <c>$all</c> read.</param>
    /// <param name="connectionString">
    /// The node's <c>esdb://</c> connection string — the HTTP management base and credentials are derived from it.
    /// </param>
    /// <param name="logger">Optional; defaults to <see cref="NullLogger"/>.</param>
    public UserDefinedIndexSource(KurrentDBClient client, string connectionString, ILogger? logger = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        (_httpBase, _user, _pass) = KurrentHttpEndpoint.Parse(connectionString);
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>The read stream (<c>$idx-user-{name}</c>) that carries <paramref name="name"/>'s indexed links.</summary>
    public static string IndexStream(string name) => IndexStreamPrefixRoot + name;

    /// <summary>
    /// Creates the index if it does not already exist (idempotent). Returns without waiting for the backfill —
    /// call <see cref="WaitUntilReadyAsync"/> before reading. No-op when <paramref name="dryRun"/> is true.
    /// </summary>
    public async Task EnsureAsync(UserDefinedIndexSpec spec, bool dryRun = false, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var name = NormalizeName(spec.Name);
        var filter = BuildFilter(spec.EventTypes);

        if (dryRun)
        {
            _logger.LogInformation("DRY RUN: would create user-defined index '{Name}' with filter [{Filter}].",
                name, filter);
            return;
        }

        var url = new Uri(_httpBase, $"v2/indexes/{Uri.EscapeDataString(name)}");
        using var http = KurrentHttpEndpoint.CreateClient(_user, _pass);
        var payload = new JsonObject { ["filter"] = filter, ["start"] = true };
        using var content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

        HttpResponseMessage resp;
        try { resp = await http.PostAsync(url, content, ct).ConfigureAwait(false); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to POST user-defined index '{Name}' at {Uri}.", name, url);
            throw;
        }

        if (resp.IsSuccessStatusCode)
        {
            _logger.LogInformation("Created (or confirmed) user-defined index '{Name}' with filter [{Filter}].",
                name, filter);
            return;
        }

        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.Conflict) // 409 INDEX_ALREADY_EXISTS — idempotent success
        {
            await WarnOnFilterDriftAsync(http, name, filter, ct).ConfigureAwait(false);
            _logger.LogInformation("User-defined index '{Name}' already exists — reusing it.", name);
            return;
        }

        _logger.LogError("Creating user-defined index '{Name}' at {Uri} failed: HTTP {Code} {Body}.",
            name, url, (int)resp.StatusCode, body);
        throw new InvalidOperationException(
            $"Creating user-defined index '{name}' failed: HTTP {(int)resp.StatusCode} at {url} — {body}");
    }

    /// <summary>
    /// Waits until the index has backfilled EXACTLY <paramref name="expectedCount"/> events — the AUTHORITATIVE
    /// readiness signal, NOT a stability heuristic. <paramref name="expectedCount"/> is the known number of
    /// matching events in the (frozen) store being indexed; obtain it from <see cref="CountMatchingAsync"/>
    /// against the same client (its <c>$all</c> is immediately consistent, so the count is exact, not a guess).
    /// </summary>
    /// <remarks>
    /// KurrentDB exposes no index checkpoint/progress (GET returns only name/filter/fields/state, and reports
    /// <c>STARTED</c> before the backfill completes), so the only authoritative signal is the count of indexed
    /// events converging to the KNOWN total. This method polls the filtered read until it returns
    /// <paramref name="expectedCount"/> events: a NotFound (index still building) or a short count keeps waiting;
    /// reaching the exact count is ready; EXCEEDING it throws (the index must never contain more than the store's
    /// matching events). Bounded by <paramref name="timeout"/> → <see cref="TimeoutException"/> reporting how far
    /// the backfill got, so a stalled/short build FAILS LOUD instead of returning a truncated merge.
    /// </remarks>
    public async Task WaitUntilReadyAsync(string name, long expectedCount, TimeSpan timeout,
        CancellationToken ct = default)
    {
        if (expectedCount < 0) throw new ArgumentOutOfRangeException(nameof(expectedCount));
        name = NormalizeName(name);
        var deadline = DateTime.UtcNow + timeout;
        var url = new Uri(_httpBase, $"v2/indexes/{Uri.EscapeDataString(name)}");
        using var http = KurrentHttpEndpoint.CreateClient(_user, _pass);

        // Phase 1: the index RESOURCE is present and STARTED (404 right after POST = still building, keep polling).
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var state = await TryGetStateAsync(http, url, name, ct).ConfigureAwait(false);
            if (state is not null && state.Contains("STARTED", StringComparison.OrdinalIgnoreCase)) break;
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException(
                    $"User-defined index '{name}' did not reach STARTED within {timeout.TotalSeconds:0}s "
                    + $"(last state '{state ?? "<not-found>"}', {url}).");
            await Task.Delay(100, ct).ConfigureAwait(false);
        }

        // Phase 2: the BACKFILL has reached the KNOWN total. STARTED is reported IMMEDIATELY (before history is
        // indexed), and the filtered read THROWS NotFound while the index is still building — that is "still
        // building", NOT "empty" (the documented gotcha). Ready = the indexed count equals expectedCount exactly.
        long last = -1;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var count = await TryCountAsync(name, ct).ConfigureAwait(false);
            if (count is { } c)
            {
                last = c;
                if (c == expectedCount)
                {
                    _logger.LogInformation("User-defined index '{Name}' ready — {Count}/{Expected} event(s) indexed.",
                        name, c, expectedCount);
                    return;
                }
                if (c > expectedCount)
                    throw new InvalidOperationException(
                        $"User-defined index '{name}' indexed {c} events but only {expectedCount} were expected — "
                        + $"the filter matched more than the store's events ({IndexStream(name)}). This indicates a "
                        + "wrong expected count or an index-definition mismatch, not a transient state.");
            }
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException(
                    $"User-defined index '{name}' backfill did not reach {expectedCount} event(s) within "
                    + $"{timeout.TotalSeconds:0}s (indexed {(last < 0 ? "<building>" : last.ToString())} so far, "
                    + $"{IndexStream(name)}). The index is not keeping up (stopped/faulted?) — failing rather than "
                    + "proceeding with a truncated merge.");
            await Task.Delay(100, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The AUTHORITATIVE count of events this index will contain: the number of events in this client's
    /// <c>$all</c> whose stored type is in <paramref name="eventTypes"/> (or every non-system event when the set
    /// is empty — the "index all" case). Because the migration creates the index AFTER the copy completes and
    /// <c>$all</c> is immediately consistent, this is an EXACT expected total, not an estimate. Skips
    /// <c>$</c>-system streams and <c>$</c>-typed events (link/tombstone markers never match a domain schema name).
    /// </summary>
    public async Task<long> CountMatchingAsync(IReadOnlySet<string> eventTypes, CancellationToken ct = default)
    {
        long n = 0;
        await foreach (var re in _client.ReadAllAsync(Direction.Forwards, Position.Start, resolveLinkTos: false,
                           cancellationToken: ct).ConfigureAwait(false))
        {
            var er = re.Event;
            if (er is null) continue;
            if (er.EventStreamId.Length > 0 && er.EventStreamId[0] == '$') continue; // system stream
            if (er.EventType.Length > 0 && er.EventType[0] == '$') continue;          // link/tombstone/system event
            if (eventTypes.Count == 0 || eventTypes.Contains(er.EventType)) n++;
        }
        return n;
    }

    /// <summary>
    /// Streams the indexed events in true <c>$all</c> COMMIT order as <see cref="RawEvent"/>s (payload/metadata
    /// as raw JSON; <see cref="RawEvent.Data"/> is <c>null</c> for non-JSON payloads). Reads the RESOLVED target
    /// events (<c>resolveLinkTos</c>), so <see cref="RawEvent.StreamId"/>/<see cref="RawEvent.EventNumber"/> are
    /// the original event's stream and revision. Call <see cref="WaitUntilReadyAsync"/> first for a complete read.
    /// </summary>
    public async IAsyncEnumerable<RawEvent> ReadAsync(string name, [EnumeratorCancellation] CancellationToken ct = default)
    {
        name = NormalizeName(name);
        var filter = StreamFilter.Prefix(IndexStream(name));
        var read = _client.ReadAllAsync(Direction.Forwards, Position.Start, filter, maxCount: long.MaxValue,
            resolveLinkTos: true, cancellationToken: ct);

        await foreach (var re in read.ConfigureAwait(false))
        {
            var er = re.Event; // resolved original (null if the link target is gone — skip, never dereference)
            if (er is null) continue;

            yield return new RawEvent
            {
                StreamId = er.EventStreamId,
                EventNumber = er.EventNumber.ToUInt64(),
                Type = er.EventType,
                Data = TryParseJson(er.Data),
                Metadata = TryParseJson(er.Metadata)
            };
        }
    }

    /// <summary>
    /// Convenience: <see cref="EnsureAsync"/> → compute the authoritative expected count
    /// (<see cref="CountMatchingAsync"/>) → <see cref="WaitUntilReadyAsync"/> → <see cref="ReadAsync"/>.
    /// </summary>
    public async IAsyncEnumerable<RawEvent> CreateWaitReadAsync(UserDefinedIndexSpec spec, TimeSpan readyTimeout,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await EnsureAsync(spec, dryRun: false, ct).ConfigureAwait(false);
        var expected = await CountMatchingAsync(spec.EventTypes, ct).ConfigureAwait(false);
        await WaitUntilReadyAsync(spec.Name, expected, readyTimeout, ct).ConfigureAwait(false);
        await foreach (var e in ReadAsync(spec.Name, ct).ConfigureAwait(false))
            yield return e;
    }

    // ---- helpers -------------------------------------------------------------------------------------------

    /// <summary>
    /// Builds the index filter as the single-argument JavaScript arrow function KurrentDB requires:
    /// <c>rec =&gt; rec.schema.name == "A" || rec.schema.name == "B"</c>. An empty set ⇒ null (index all records).
    /// Each event-type name is escaped for the JS double-quoted string context so a name containing a quote,
    /// backslash or control character cannot break out of the literal (or silently corrupt the filter).
    /// </summary>
    internal static string? BuildFilter(IReadOnlySet<string> eventTypes)
    {
        if (eventTypes.Count == 0) return null;
        // Ordinal-sorted so the generated filter is STABLE for the same set (matters for drift detection).
        var terms = eventTypes.OrderBy(t => t, StringComparer.Ordinal)
            .Select(t => $"rec.schema.name == \"{EscapeJsString(t)}\"");
        return "rec => " + string.Join(" || ", terms);
    }

    /// <summary>Escapes a string for a JavaScript double-quoted literal (backslash, quote, control chars).</summary>
    internal static string EscapeJsString(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        foreach (var ch in s)
            sb.Append(ch switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                < ' ' => $"\\u{(int)ch:x4}",
                _ => ch.ToString()
            });
        return sb.ToString();
    }

    /// <summary>
    /// Normalises an arbitrary name (e.g. an <c>[OutputStream]</c> stream id like <c>FooModel_v1</c>) to a valid
    /// KurrentDB index name: lower-case; every character outside <c>[a-z0-9_]</c> becomes <c>-</c>. Names that
    /// would collide after lower-casing/replacement should be disambiguated by the caller (the runner appends a
    /// short content hash — see <see cref="UserDefinedIndexCopyContext"/>).
    /// </summary>
    internal static string NormalizeName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) throw new ArgumentException("Index name must be non-empty.", nameof(raw));
        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            var c = char.ToLowerInvariant(ch);
            sb.Append(c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '-' ? c : '-');
        }
        return sb.ToString();
    }

    private async Task<string?> TryGetStateAsync(HttpClient http, Uri url, string name, CancellationToken ct)
    {
        HttpResponseMessage resp;
        try { resp = await http.GetAsync(url, ct).ConfigureAwait(false); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET user-defined index '{Name}' at {Uri} failed transiently — retrying.", name, url);
            return null;
        }

        if (resp.StatusCode == HttpStatusCode.NotFound) return null; // still building / not yet visible
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _logger.LogWarning("GET user-defined index '{Name}' at {Uri}: HTTP {Code} {Body}.",
                name, url, (int)resp.StatusCode, body);
            return null;
        }

        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var node = JsonNode.Parse(json);
        return node?["index"]?["state"]?.GetValue<string>();
    }

    private async Task WarnOnFilterDriftAsync(HttpClient http, string name, string? requestedFilter, CancellationToken ct)
    {
        var url = new Uri(_httpBase, $"v2/indexes/{Uri.EscapeDataString(name)}");
        try
        {
            using var resp = await http.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return;
            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var existing = JsonNode.Parse(json)?["index"]?["filter"]?.GetValue<string>();
            var existingNorm = string.IsNullOrEmpty(existing) ? null : existing;
            if (!string.Equals(existingNorm, requestedFilter, StringComparison.Ordinal))
                _logger.LogWarning(
                    "User-defined index '{Name}' already exists with a DIFFERENT filter (existing [{Existing}], "
                    + "requested [{Requested}]) — the existing definition is kept.", name, existingNorm ?? "<all>",
                    requestedFilter ?? "<all>");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read existing filter for user-defined index '{Name}'.", name);
        }
    }

    // O(n) count of the indexed events EXACTLY as ReadAsync would yield them: same filtered read, same
    // resolveLinkTos:true, same skip of a null-resolving (deleted-target) link — so the readiness gate count can
    // NEVER diverge from what the reader emits (a resolveLinkTos:false count could reach the expected total while
    // ReadAsync silently skips a dead link and yields fewer). Returns null when the index read stream does not
    // yet exist — KurrentDB throws NotFound while the index is still BUILDING, which must read as "not ready",
    // NOT "empty". n is the merge size (~thousands), so a full scan per poll is fine.
    private async Task<long?> TryCountAsync(string name, CancellationToken ct)
    {
        var filter = StreamFilter.Prefix(IndexStream(name));
        var read = _client.ReadAllAsync(Direction.Forwards, Position.Start, filter, maxCount: long.MaxValue,
            resolveLinkTos: true, cancellationToken: ct);
        var e = read.GetAsyncEnumerator(ct);
        long n = 0;
        try
        {
            while (true)
            {
                try
                {
                    if (!await e.MoveNextAsync().ConfigureAwait(false)) break;
                }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
                {
                    return null; // index read stream not materialised yet → still building
                }
                if (e.Current.Event is null) continue; // dead link — ReadAsync skips it, so it must not be counted
                n++;
            }
        }
        finally { await e.DisposeAsync().ConfigureAwait(false); }
        return n;
    }

    private static JsonNode? TryParseJson(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.IsEmpty) return null;
        try
        {
            var reader = new Utf8JsonReader(bytes.Span);
            return JsonNode.Parse(ref reader);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
