using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using KurrentDB.Client;
using MicroPlumberd.Migration;
using MicroPlumberd.Testing;
using Xunit;
using Xunit.Abstractions;

namespace MicroPlumberd.Migration.Tests;

/// <summary>
/// SPIKE — second empirical batch against REAL KurrentDB 26.1, closing the last design questions:
///  SPIKE-8  FIELD-VALUE PARTITION (the potential shape-B rescue): an index defined with a "fields" selector
///           (rec => rec.value.&lt;field&gt;) exposes a per-value partition stream "$idx-user-&lt;name&gt;:&lt;value&gt;". Does
///           reading/subscribing a single partition deliver ONLY that key's events, in commit order? If yes,
///           per-key lookup (Inbox/Outbox routing) MAY be index-backable and shape-B is NOT permanently excluded.
///  SPIKE-9  DIRECT-SUBSCRIBE DEFINITIVE: exhaustively confirm the whole-index stream "$idx-user-&lt;name&gt;" is NOT
///           directly consumable — SubscribeToStream AND ReadStream, resolveLinkTos both true and false — i.e. the
///           documented recipe is the filtered-$all prefix, nothing else.
///  SPIKE-10 CaughtUp + no-count-gate: on the working filtered SubscribeToAll(prefix) path, does StreamMessage.CaughtUp
///           fire at the history→live boundary (read models need it to mark ready), and does the live path tail
///           WITHOUT any count-based readiness gate (the offline WaitUntilReadyAsync count-gate is offline-only)?
///
/// NOTE on SPIKE-4 (first batch): that test probed body access via rec.data/rec.body and saw zero — but the
/// KurrentDB 26.1 body accessor is rec.value (per docs). SPIKE-8 here re-tests with the correct accessor + the
/// dedicated "fields" mechanism, and supersedes SPIKE-4's "body not accessible" conclusion where they differ.
/// </summary>
[Trait("Category", "Integration")]
public class FieldPartitionAndDirectSubscribeSpikes(ITestOutputHelper output)
{
    private sealed class Store : IAsyncDisposable
    {
        private readonly List<EventStoreServer> _servers = new();
        public async Task<(KurrentDBClient Client, string Conn)> NewAsync(string tag)
        {
            var es = EventStoreServer.Create($"mp-fp-{tag}-{Guid.NewGuid():N}");
            _servers.Add(es);
            await es.StartInDocker(inMemory: true);
            var s = es.GetEventStoreSettings();
            return (new KurrentDBClient(s), es.HttpUrl.ToString());
        }
        public async ValueTask DisposeAsync() { foreach (var s in _servers) await s.DisposeAsync(); }
    }

    private static EventData Bodied(string type, int ord, string someId) =>
        new(Uuid.NewUuid(), type, Encoding.UTF8.GetBytes($"{{\"Ord\":{ord},\"SomeId\":\"{someId}\"}}"));

    // Raw filtered-$all read over an arbitrary stream PREFIX (resolveLinkTos), collecting the resolved body "Ord".
    private static async Task<List<int>> ReadPrefixOrdsAsync(KurrentDBClient client, string prefix, CancellationToken ct = default)
    {
        var ords = new List<int>();
        var read = client.ReadAllAsync(Direction.Forwards, Position.Start, StreamFilter.Prefix(prefix),
            maxCount: long.MaxValue, resolveLinkTos: true, cancellationToken: ct);
        var e = read.GetAsyncEnumerator(ct);
        try
        {
            while (true)
            {
                try { if (!await e.MoveNextAsync()) break; }
                catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.NotFound) { break; }
                if (e.Current.Event is null) continue;
                ords.Add(JsonNode.Parse(e.Current.Event.Data.Span)?["Ord"]?.GetValue<int>() ?? -1);
            }
        }
        finally { await e.DisposeAsync(); }
        return ords;
    }

    private static async Task<bool> WaitUntil(Func<Task<bool>> cond, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline) { if (await cond()) return true; await Task.Delay(200); }
        return await cond();
    }

    // =====================================================================================================
    // SPIKE-8 — FIELD-VALUE PARTITION. Define an index keyed by a body field (selector rec => rec.value.SomeId)
    // and confirm the per-value partition stream "$idx-user-<name>:<value>" delivers ONLY that key's events in
    // commit order — the shape-B (per-key lookup) rescue test.
    // =====================================================================================================
    [Fact]
    public async Task Spike8_field_value_partition_delivers_single_key_in_commit_order()
    {
        await using var stores = new Store();
        var (client, conn) = await stores.NewAsync("s8");

        // Interleaved two partitions: alpha gets Ord 0,2,4 ; beta gets 1,3 (distinct values, no prefix overlap).
        await client.AppendToStreamAsync("S8-1", StreamState.NoStream, new[]
        {
            Bodied("UdiC", 0, "alpha"), Bodied("UdiC", 1, "beta"), Bodied("UdiC", 2, "alpha"),
            Bodied("UdiC", 3, "beta"), Bodied("UdiC", 4, "alpha")
        });

        var (httpBase, user, pass) = KurrentHttpEndpoint.Parse(conn);
        using var http = KurrentHttpEndpoint.CreateClient(user, pass);

        const string name = "orders-by-key";
        var url = new Uri(httpBase, $"v2/indexes/{name}");
        var payload = new JsonObject
        {
            ["filter"] = "rec => rec.schema.name == 'UdiC'",
            ["fields"] = new JsonArray(new JsonObject
            {
                ["name"] = "someid",
                ["selector"] = "rec => rec.value.SomeId",
                ["type"] = "INDEX_FIELD_TYPE_STRING"
            }),
            ["start"] = true
        };
        using var content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        var resp = await http.PostAsync(url, content);
        var body = await resp.Content.ReadAsStringAsync();
        output.WriteLine($"SPIKE-8 POST (fields=someid rec.value.SomeId) -> HTTP {(int)resp.StatusCode}: {body}");
        resp.IsSuccessStatusCode.Should().BeTrue("a field-keyed index definition must be accepted");

        var whole = UserDefinedIndexSource.IndexStream(name);           // "$idx-user-orders-by-key"
        var partAlpha = $"{whole}:alpha";                                // "$idx-user-orders-by-key:alpha"
        var partBeta = $"{whole}:beta";

        // Whole index must backfill all 5 (in commit order) — also our readiness signal.
        var wholeReady = await WaitUntil(async () => (await ReadPrefixOrdsAsync(client, whole)).Count >= 5, TimeSpan.FromSeconds(30));
        var wholeOrds = await ReadPrefixOrdsAsync(client, whole);
        output.WriteLine($"SPIKE-8 whole index (ready={wholeReady}): {string.Join(",", wholeOrds)}");

        // THE shape-B test: the alpha partition must contain ONLY alpha's events (0,2,4), in commit order.
        var alpha = await ReadPrefixOrdsAsync(client, partAlpha);
        var beta = await ReadPrefixOrdsAsync(client, partBeta);
        output.WriteLine($"SPIKE-8 partition :alpha = {string.Join(",", alpha)}  (expect 0,2,4)");
        output.WriteLine($"SPIKE-8 partition :beta  = {string.Join(",", beta)}  (expect 1,3)");

        // Live: append more to alpha AFTER building; the partition read must pick it up in commit order.
        await client.AppendToStreamAsync("S8-2", StreamState.NoStream, [Bodied("UdiC", 5, "beta"), Bodied("UdiC", 6, "alpha")]);
        var alphaLive = await WaitUntil(async () => (await ReadPrefixOrdsAsync(client, partAlpha)).Contains(6), TimeSpan.FromSeconds(30));
        var alpha2 = await ReadPrefixOrdsAsync(client, partAlpha);
        output.WriteLine($"SPIKE-8 partition :alpha after live (picked6={alphaLive}) = {string.Join(",", alpha2)} (expect 0,2,4,6)");

        // ASSERTIONS — encode the shape-B verdict.
        wholeOrds.Should().Equal(new[] { 0, 1, 2, 3, 4 }, "whole field-index still reads all events in commit order");
        alpha.Should().Equal(new[] { 0, 2, 4 }, "the :alpha partition delivers ONLY alpha's events, in commit order");
        beta.Should().Equal(new[] { 1, 3 }, "the :beta partition delivers ONLY beta's events, in commit order");
        alpha2.Should().Equal(new[] { 0, 2, 4, 6 }, "the partition live-updates in commit order (shape-B is index-backable)");
    }

    // =====================================================================================================
    // SPIKE-11 — SUBSCRIBE (catch-up→live PUSH) to a SINGLE field partition. SPIKE-8 proved partition CONTENT
    // via read; this proves the go/no-go path the design actually uses: an OPEN SubscribeToAll over
    // StreamFilter.Prefix("$idx-user-<name>:<value>") delivers ONLY that key's events on catch-up AND pushes only
    // that key's live appends (ignoring other keys), in commit order — i.e. shape-B (per-key lookup) is a real
    // live subscription, not just a re-readable snapshot.
    // =====================================================================================================
    [Fact]
    public async Task Spike11_subscribe_single_field_partition_pushes_only_that_key_catchup_and_live()
    {
        await using var stores = new Store();
        var (client, conn) = await stores.NewAsync("s11");

        await client.AppendToStreamAsync("S11-1", StreamState.NoStream, new[]
        {
            Bodied("UdiC", 0, "alpha"), Bodied("UdiC", 1, "beta"), Bodied("UdiC", 2, "alpha"),
            Bodied("UdiC", 3, "beta"), Bodied("UdiC", 4, "alpha")
        });

        var (httpBase, user, pass) = KurrentHttpEndpoint.Parse(conn);
        using var http = KurrentHttpEndpoint.CreateClient(user, pass);
        const string name = "s11-by-key";
        var url = new Uri(httpBase, $"v2/indexes/{name}");
        var payload = new JsonObject
        {
            ["filter"] = "rec => rec.schema.name == 'UdiC'",
            ["fields"] = new JsonArray(new JsonObject
            { ["name"] = "someid", ["selector"] = "rec => rec.value.SomeId", ["type"] = "INDEX_FIELD_TYPE_STRING" }),
            ["start"] = true
        };
        using var content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        (await http.PostAsync(url, content)).EnsureSuccessStatusCode();

        var whole = UserDefinedIndexSource.IndexStream(name);
        var partAlpha = $"{whole}:alpha";
        // wait until the alpha partition has backfilled its 3 (via read) before opening the subscription
        await WaitUntil(async () => (await ReadPrefixOrdsAsync(client, partAlpha)).Count >= 3, TimeSpan.FromSeconds(30));

        var received = new ConcurrentQueue<int>();
        var caughtUp = false;
        using var cts = new CancellationTokenSource();
        var pump = Task.Run(async () =>
        {
            var filter = new SubscriptionFilterOptions(StreamFilter.Prefix(partAlpha));
            await using var sub = client.SubscribeToAll(FromAll.Start, resolveLinkTos: true, filterOptions: filter,
                cancellationToken: cts.Token);
            try
            {
                await foreach (var m in sub.Messages.WithCancellation(cts.Token))
                    switch (m)
                    {
                        case StreamMessage.Event(var e):
                            received.Enqueue(JsonNode.Parse(e.Event.Data.Span)?["Ord"]?.GetValue<int>() ?? -1);
                            break;
                        case StreamMessage.CaughtUp: caughtUp = true; break;
                    }
            }
            catch (OperationCanceledException) { }
        });

        var gotHistory = await WaitUntil(() => Task.FromResult(received.Count >= 3), TimeSpan.FromSeconds(30));
        output.WriteLine($"SPIKE-11 partition :alpha catch-up (n={received.Count} caughtUp={caughtUp}): {string.Join(",", received)} (expect 0,2,4)");

        // Live: append to BOTH keys. The alpha subscription must push ONLY 6 (alpha), NOT 5 (beta).
        await client.AppendToStreamAsync("S11-2", StreamState.NoStream, [Bodied("UdiC", 5, "beta"), Bodied("UdiC", 6, "alpha")]);
        var gotLive = await WaitUntil(() => Task.FromResult(received.Contains(6)), TimeSpan.FromSeconds(30));
        await Task.Delay(1000); // give any erroneous beta(5) delivery a chance to show up

        cts.Cancel(); await pump;

        output.WriteLine($"SPIKE-11 partition :alpha after live (gotLive={gotLive}): {string.Join(",", received)} (expect 0,2,4,6 — NO 5)");
        gotHistory.Should().BeTrue("the partition subscription must catch up over that key's history");
        gotLive.Should().BeTrue("the partition subscription must PUSH that key's live appends");
        received.Should().Equal(new[] { 0, 2, 4, 6 },
            "the partition subscription delivers ONLY alpha (0,2,4,6) — never beta's 1,3,5 — in commit order");
        received.Should().NotContain(5, "beta's live append must NOT leak into the alpha partition subscription");
    }

    // =====================================================================================================
    // SPIKE-12 ($ce-category / stream-selection filter expressivity) — a shape-A projection built on
    // fromCategory('X') / $ce-X selects by STREAM CATEGORY, not event type. Can a user-defined index filter
    // express that? Docs (features/indexes/user-defined.html) expose the stream name as rec.position.stream (no
    // dedicated category property; category = prefix before '-'). Test category-style filters on rec.position.stream
    // against a schema-name CONTROL that MUST match (so an empty category result is a real FAIL, not a broken harness).
    // If PASS: EnsureFieldPartitionedIndexAsync can use a direct category filter. If FAIL: it must enumerate the
    // category's known event types (with the "misses a type added later" caveat).
    // =====================================================================================================
    [Fact]
    public async Task Spike12_category_stream_selection_filter_expressivity()
    {
        await using var stores = new Store();
        var (client, conn) = await stores.NewAsync("s12");

        // SAME event type "Ev" in TWO categories so ONLY the stream/category can discriminate them.
        // Category X (streams "X-..") gets Ord 0,2 ; category Y gets Ord 1,3 — interleaved in $all commit order.
        await client.AppendToStreamAsync("X-1", StreamState.NoStream,
            [new EventData(Uuid.NewUuid(), "Ev", Encoding.UTF8.GetBytes("{\"Ord\":0}"))]);
        await client.AppendToStreamAsync("Y-1", StreamState.NoStream,
            [new EventData(Uuid.NewUuid(), "Ev", Encoding.UTF8.GetBytes("{\"Ord\":1}"))]);
        await client.AppendToStreamAsync("X-2", StreamState.NoStream,
            [new EventData(Uuid.NewUuid(), "Ev", Encoding.UTF8.GetBytes("{\"Ord\":2}"))]);
        await client.AppendToStreamAsync("Y-2", StreamState.NoStream,
            [new EventData(Uuid.NewUuid(), "Ev", Encoding.UTF8.GetBytes("{\"Ord\":3}"))]);

        var (httpBase, user, pass) = KurrentHttpEndpoint.Parse(conn);
        using var http = KurrentHttpEndpoint.CreateClient(user, pass);

        async Task<(HttpStatusCode Status, string Body, List<int> Ords)> TryFilter(string name, string filter, int expectMatches)
        {
            var url = new Uri(httpBase, $"v2/indexes/{name}");
            using var content = new StringContent(new JsonObject { ["filter"] = filter, ["start"] = true }.ToJsonString(),
                Encoding.UTF8, "application/json");
            var r = await http.PostAsync(url, content);
            var body = await r.Content.ReadAsStringAsync();
            var ords = new List<int>();
            if (r.IsSuccessStatusCode)
            {
                await WaitUntil(async () => (await ReadPrefixOrdsAsync(client, UserDefinedIndexSource.IndexStream(name))).Count >= expectMatches, TimeSpan.FromSeconds(15));
                ords = await ReadPrefixOrdsAsync(client, UserDefinedIndexSource.IndexStream(name));
            }
            return (r.StatusCode, body, ords);
        }

        // CONTROL — schema-name filter that MUST match all 4 (proves the harness/index works on this store).
        var ctrl = await TryFilter("s12-ctrl", "rec => rec.schema.name == 'Ev'", 4);
        output.WriteLine($"SPIKE-12 CONTROL rec.schema.name=='Ev' -> HTTP {(int)ctrl.Status}: ords={string.Join(",", ctrl.Ords)} (expect 0,1,2,3)");

        // CATEGORY filters on rec.position.stream — two forms (startsWith, and split-before-dash).
        var startsWith = await TryFilter("s12-startswith", "rec => rec.position.stream.startsWith('X-')", 2);
        output.WriteLine($"SPIKE-12 rec.position.stream.startsWith('X-') -> HTTP {(int)startsWith.Status}: {(startsWith.Status == HttpStatusCode.OK ? $"ords={string.Join(",", startsWith.Ords)}" : startsWith.Body)} (expect 0,2 if category-selection works)");

        var split = await TryFilter("s12-split", "rec => rec.position.stream.split('-')[0] == 'X'", 2);
        output.WriteLine($"SPIKE-12 rec.position.stream.split('-')[0]=='X' -> HTTP {(int)split.Status}: {(split.Status == HttpStatusCode.OK ? $"ords={string.Join(",", split.Ords)}" : split.Body)} (expect 0,2 if supported)");

        // Verdict is logged verbatim above; assert only the control (harness soundness) hard, and record whether
        // ANY category form worked so the report can state PASS/FAIL definitively.
        ctrl.Ords.Should().Equal(new[] { 0, 1, 2, 3 }, "CONTROL: schema-name filter works — the store/index harness is sound");
        var categoryWorks = (startsWith.Status == HttpStatusCode.OK && startsWith.Ords.SequenceEqual(new[] { 0, 2 }))
                            || (split.Status == HttpStatusCode.OK && split.Ords.SequenceEqual(new[] { 0, 2 }));
        output.WriteLine($"SPIKE-12 VERDICT: category/stream selection expressible = {categoryWorks} " +
                         "(if false, EnsureFieldPartitionedIndexAsync must enumerate the category's event types)");
    }

    // =====================================================================================================
    // SPIKE-9 — DIRECT-SUBSCRIBE DEFINITIVE. The whole-index stream "$idx-user-<name>" is NOT directly
    // consumable: SubscribeToStream and ReadStream both yield nothing (resolveLinkTos true AND false). The ONLY
    // documented recipe is the filtered-$all prefix. This nails "can you subscribe to the index like any stream?".
    // =====================================================================================================
    [Fact]
    public async Task Spike9_direct_subscribe_and_read_on_index_stream_yield_nothing()
    {
        await using var stores = new Store();
        var (client, conn) = await stores.NewAsync("s9");
        await client.AppendToStreamAsync("S9-1", StreamState.NoStream,
            [Bodied("UdiA", 0, "x"), Bodied("UdiB", 1, "y"), Bodied("UdiA", 2, "x")]);

        var src = new UserDefinedIndexSource(client, conn);
        var spec = new UserDefinedIndexSpec { Name = "s9-merge", EventTypes = new HashSet<string> { "UdiA", "UdiB" } };
        await src.EnsureAsync(spec);
        await src.WaitUntilReadyAsync(spec.Name, expectedCount: 3, TimeSpan.FromSeconds(30));
        var stream = UserDefinedIndexSource.IndexStream(spec.Name);

        // Sanity: the DOCUMENTED path (filtered $all prefix) DOES work — proves the index has data to find.
        var viaFilter = await ReadPrefixOrdsAsync(client, stream);
        output.WriteLine($"SPIKE-9 filtered-$all prefix (documented recipe) = {string.Join(",", viaFilter)} (expect 0,1,2)");
        viaFilter.Should().Equal(new[] { 0, 1, 2 }, "the documented filtered-$all recipe must work");

        // ReadStream x resolveLinkTos matrix.
        foreach (var rlt in new[] { true, false })
        {
            var res = client.ReadStreamAsync(Direction.Forwards, stream, StreamPosition.Start, resolveLinkTos: rlt);
            var state = await res.ReadState;
            var n = state == ReadState.StreamNotFound ? 0 : await res.CountAsync();
            output.WriteLine($"SPIKE-9 ReadStream('{stream}', resolveLinkTos={rlt}) -> ReadState={state}, events={n}");
            n.Should().Be(0, $"direct ReadStream on the index stream yields nothing (resolveLinkTos={rlt})");
        }

        // SubscribeToStream x resolveLinkTos matrix — bounded wait, then confirm nothing arrived.
        foreach (var rlt in new[] { true, false })
        {
            var got = new ConcurrentQueue<int>();
            string? err = null;
            using var cts = new CancellationTokenSource();
            var pump = Task.Run(async () =>
            {
                try
                {
                    await using var sub = client.SubscribeToStream(stream, FromStream.Start, resolveLinkTos: rlt,
                        cancellationToken: cts.Token);
                    await foreach (var m in sub.Messages.WithCancellation(cts.Token))
                        if (m is StreamMessage.Event) got.Enqueue(1);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { err = $"{ex.GetType().Name}: {ex.Message}"; }
            });
            await WaitUntil(() => Task.FromResult(got.Count >= 3), TimeSpan.FromSeconds(8));
            cts.Cancel(); await pump;
            output.WriteLine($"SPIKE-9 SubscribeToStream('{stream}', resolveLinkTos={rlt}) -> events={got.Count}, err={err ?? "<none>"}");
            got.Count.Should().Be(0, $"direct SubscribeToStream on the index stream yields nothing (resolveLinkTos={rlt})");
        }
    }

    // =====================================================================================================
    // SPIKE-10 — CaughtUp signal + no-count-gate on the WORKING filtered-$all catch-up path.
    // =====================================================================================================
    [Fact]
    public async Task Spike10_caughtup_fires_and_live_path_needs_no_count_gate()
    {
        await using var stores = new Store();
        var (client, conn) = await stores.NewAsync("s10");
        await client.AppendToStreamAsync("S10-1", StreamState.NoStream,
            [Bodied("UdiA", 0, "x"), Bodied("UdiB", 1, "y"), Bodied("UdiA", 2, "x")]);

        var src = new UserDefinedIndexSource(client, conn);
        var spec = new UserDefinedIndexSpec { Name = "s10-merge", EventTypes = new HashSet<string> { "UdiA", "UdiB" } };
        await src.EnsureAsync(spec);
        await src.WaitUntilReadyAsync(spec.Name, expectedCount: 3, TimeSpan.FromSeconds(30));
        var stream = UserDefinedIndexSource.IndexStream(spec.Name);

        var received = new ConcurrentQueue<int>();
        var caughtUpAfter = -1;          // received.Count at the moment CaughtUp fired
        var caughtUpFired = false;
        using var cts = new CancellationTokenSource();

        // NOTE: NO WaitUntilReadyAsync / count precondition on this LIVE path — just subscribe + tail.
        var pump = Task.Run(async () =>
        {
            var filter = new SubscriptionFilterOptions(StreamFilter.Prefix(stream));
            await using var sub = client.SubscribeToAll(FromAll.Start, resolveLinkTos: true, filterOptions: filter,
                cancellationToken: cts.Token);
            try
            {
                await foreach (var m in sub.Messages.WithCancellation(cts.Token))
                {
                    switch (m)
                    {
                        case StreamMessage.Event(var e):
                            received.Enqueue(JsonNode.Parse(e.Event.Data.Span)?["Ord"]?.GetValue<int>() ?? -1);
                            break;
                        case StreamMessage.CaughtUp:
                            caughtUpFired = true;
                            caughtUpAfter = received.Count;
                            break;
                    }
                }
            }
            catch (OperationCanceledException) { }
        });

        // History (0,1,2) then wait for CaughtUp.
        await WaitUntil(() => Task.FromResult(received.Count >= 3), TimeSpan.FromSeconds(30));
        var sawCaughtUp = await WaitUntil(() => Task.FromResult(caughtUpFired), TimeSpan.FromSeconds(15));
        output.WriteLine($"SPIKE-10 CaughtUp fired={caughtUpFired} afterN={caughtUpAfter}; history={string.Join(",", received)}");

        // Live append AFTER caught-up — must tail with no count gate.
        await client.AppendToStreamAsync("S10-2", StreamState.NoStream, [Bodied("UdiB", 3, "y"), Bodied("UdiA", 4, "x")]);
        var tailed = await WaitUntil(() => Task.FromResult(received.Count >= 5), TimeSpan.FromSeconds(30));
        output.WriteLine($"SPIKE-10 live tail (no count-gate) got={tailed}; all={string.Join(",", received)}");

        cts.Cancel(); await pump;

        sawCaughtUp.Should().BeTrue("StreamMessage.CaughtUp must fire so a read model can mark itself ready");
        caughtUpAfter.Should().BeGreaterThanOrEqualTo(3, "CaughtUp should fire at/after the full history is delivered");
        tailed.Should().BeTrue("the live subscription tails new appends with NO count-based readiness gate");
        received.Should().Equal(Enumerable.Range(0, 5), "history + live in commit order");
    }
}
