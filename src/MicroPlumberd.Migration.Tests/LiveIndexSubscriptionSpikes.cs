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
/// SPIKE — throwaway empirical feasibility investigation (NOT production). Answers ONE yes/no question against a
/// REAL KurrentDB 26.1 (MicroPlumberd.Testing Docker server, NEVER Testcontainers): can a KurrentDB 26.1
/// USER-DEFINED INDEX (<c>$idx-user-&lt;name&gt;</c>) back a LIVE read-model merge stream — i.e. replace a
/// <c>fromStreams([$et-A,$et-B]).linkTo(out)</c> join projection — such that a read model can SUBSCRIBE
/// (catch-up over history THEN tail live) and receive events in COMMIT order as new ones are appended?
///
/// The existing <see cref="UserDefinedIndexSource"/> only does a BOUNDED historical read; these spikes probe the
/// LIVE behaviour. Each test captures ACTUAL observed evidence (via <see cref="ITestOutputHelper"/>) and asserts.
///
/// SPIKE-1: does the index LIVE-UPDATE and stay COMMIT-ordered when more interleaved events are appended AFTER it
///          is already built (bounded re-read).
/// SPIKE-2: can KurrentDB.Client SUBSCRIBE (catch-up→live) to the index and RECEIVE post-subscription appends
///          through the same open subscription, in commit order — via SubscribeToStream on the index stream AND
///          via filtered SubscribeToAll (the ReadAsync analog). Reports exactly which API works.
/// SPIKE-3: can a position in the index stream be recorded, the subscription stopped, and RESUMED from it (as a
///          restarting read model must) — receiving ONLY the new events.
/// SPIKE-4: can a user-defined index express BODY-PROPERTY routing (the <c>linkTo('cat-'+e.body.SomeId)</c>
///          lookup-projection case), or is it limited to schema/stream filtering.
/// </summary>
[Trait("Category", "Integration")]
public class LiveIndexSubscriptionSpikes(ITestOutputHelper output)
{
    private sealed class Store : IAsyncDisposable
    {
        private readonly List<EventStoreServer> _servers = new();

        public async Task<(KurrentDBClient Client, string Conn)> NewAsync(string tag)
        {
            var es = EventStoreServer.Create($"mp-live-{tag}-{Guid.NewGuid():N}");
            _servers.Add(es);
            await es.StartInDocker(inMemory: true);
            var s = es.GetEventStoreSettings();
            return (new KurrentDBClient(s), es.HttpUrl.ToString());
        }

        // Variant that also hands back a persistent-subscriptions client (for the persistent-sub spike).
        public async Task<(KurrentDBClient Client, KurrentDBPersistentSubscriptionsClient Persistent, string Conn)> NewWithPsAsync(string tag)
        {
            var es = EventStoreServer.Create($"mp-live-{tag}-{Guid.NewGuid():N}");
            _servers.Add(es);
            await es.StartInDocker(inMemory: true);
            var s = es.GetEventStoreSettings();
            return (new KurrentDBClient(s), new KurrentDBPersistentSubscriptionsClient(s), es.HttpUrl.ToString());
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var s in _servers) await s.DisposeAsync();
        }
    }

    private static EventData Typed(string type, int ord) =>
        new(Uuid.NewUuid(), type, Encoding.UTF8.GetBytes($"{{\"Ord\":{ord}}}"));

    private static EventData Bodied(string type, int ord, string someId) =>
        new(Uuid.NewUuid(), type, Encoding.UTF8.GetBytes($"{{\"Ord\":{ord},\"SomeId\":\"{someId}\"}}"));

    private static async Task<List<int>> ReadOrdsAsync(UserDefinedIndexSource src, string name, CancellationToken ct = default)
    {
        var ords = new List<int>();
        await foreach (var e in src.ReadAsync(name, ct))
            ords.Add(e.Data?["Ord"]?.GetValue<int>() ?? -1);
        return ords;
    }

    private static async Task<bool> WaitUntil(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return true;
            await Task.Delay(200);
        }
        return await condition();
    }

    // =====================================================================================================
    // SPIKE-1 — LIVE TAIL ORDERING (the load-bearing test): after the index is built over an initial
    // interleaved batch, append MORE interleaved events and verify the index picks them up (start:true tails)
    // AND keeps them in commit order 0..N-1 — NOT re-clustered, NOT stale.
    // =====================================================================================================
    [Fact]
    public async Task Spike1_index_live_updates_and_stays_commit_ordered_when_more_events_appended()
    {
        await using var stores = new Store();
        var (client, conn) = await stores.NewAsync("s1");

        // Initial interleaved batch A0,B1,A2,B3,A4.
        await client.AppendToStreamAsync("S1-a", StreamState.NoStream,
            [Typed("UdiA", 0), Typed("UdiB", 1), Typed("UdiA", 2), Typed("UdiB", 3), Typed("UdiA", 4)]);

        var src = new UserDefinedIndexSource(client, conn);
        var spec = new UserDefinedIndexSpec { Name = "s1-merge", EventTypes = new HashSet<string> { "UdiA", "UdiB" } };
        await src.EnsureAsync(spec);
        await src.WaitUntilReadyAsync(spec.Name, expectedCount: 5, TimeSpan.FromSeconds(30));

        var before = await ReadOrdsAsync(src, spec.Name);
        output.WriteLine("SPIKE-1 before live-append: " + string.Join(",", before));
        before.Should().Equal(new[] { 0, 1, 2, 3, 4 });

        // NOW append MORE interleaved events to a DIFFERENT stream (later commit positions 5..9).
        await client.AppendToStreamAsync("S1-b", StreamState.NoStream,
            [Typed("UdiB", 5), Typed("UdiA", 6), Typed("UdiB", 7), Typed("UdiA", 8), Typed("UdiB", 9)]);

        // Poll the index (bounded re-read) until it reflects all 10, capturing whether it live-updates at all.
        List<int> after = new();
        var reached = await WaitUntil(async () =>
        {
            after = await ReadOrdsAsync(src, spec.Name);
            return after.Count >= 10;
        }, TimeSpan.FromSeconds(30));

        output.WriteLine($"SPIKE-1 after live-append (reached10={reached}): " + string.Join(",", after));

        reached.Should().BeTrue("a start:true user-defined index must keep indexing events appended AFTER creation");
        after.Should().Equal(Enumerable.Range(0, 10),
            "newly-appended interleaved events must land in the index in COMMIT order 0..9, not re-clustered");
    }

    // =====================================================================================================
    // SPIKE-2 — SUBSCRIBE (catch-up → live), not just read. Open a subscription BEFORE the live appends and
    // prove the post-subscription appends arrive through the SAME open subscription, in commit order.
    // Tries BOTH mechanisms and reports which works.
    // =====================================================================================================

    // 2a: SubscribeToStream on the index stream itself ($idx-user-<name>).
    [Fact]
    public async Task Spike2a_subscribe_to_index_stream_catches_up_then_receives_live_appends_in_order()
    {
        await using var stores = new Store();
        var (client, conn) = await stores.NewAsync("s2a");

        await client.AppendToStreamAsync("S2a-a", StreamState.NoStream,
            [Typed("UdiA", 0), Typed("UdiB", 1), Typed("UdiA", 2)]);

        var src = new UserDefinedIndexSource(client, conn);
        var spec = new UserDefinedIndexSpec { Name = "s2a-merge", EventTypes = new HashSet<string> { "UdiA", "UdiB" } };
        await src.EnsureAsync(spec);
        await src.WaitUntilReadyAsync(spec.Name, expectedCount: 3, TimeSpan.FromSeconds(30));

        var indexStream = UserDefinedIndexSource.IndexStream(spec.Name); // "$idx-user-s2a-merge"
        var received = new ConcurrentQueue<int>();
        using var cts = new CancellationTokenSource();
        string? subscribeError = null;

        var pump = Task.Run(async () =>
        {
            try
            {
                await using var sub = client.SubscribeToStream(indexStream, FromStream.Start, resolveLinkTos: true,
                    cancellationToken: cts.Token);
                await foreach (var m in sub.Messages.WithCancellation(cts.Token))
                {
                    if (m is StreamMessage.Event(var e))
                    {
                        var ord = JsonNode.Parse(e.Event.Data.Span)?["Ord"]?.GetValue<int>() ?? -1;
                        received.Enqueue(ord);
                    }
                }
            }
            catch (OperationCanceledException) { /* expected on stop */ }
            catch (Exception ex) { subscribeError = $"{ex.GetType().Name}: {ex.Message}"; }
        });

        // Catch-up: the 3 historical events should arrive.
        var caughtUp = await WaitUntil(() => Task.FromResult(received.Count >= 3), TimeSpan.FromSeconds(30));
        output.WriteLine($"SPIKE-2a subscribe error: {subscribeError ?? "<none>"}");
        output.WriteLine($"SPIKE-2a after catch-up (caughtUp={caughtUp}): " + string.Join(",", received));

        if (subscribeError is not null)
        {
            // CRITICAL finding path — surface the exact error and stop (cannot subscribe to the index stream).
            cts.Cancel();
            subscribeError.Should().BeNull(
                "SUBSCRIBING to $idx-user-<name> via SubscribeToStream must work for an index-backed live read model");
        }

        caughtUp.Should().BeTrue("the subscription must catch up over the index's history");

        // LIVE: append AFTER the subscription is open; the SAME subscription must push them.
        await client.AppendToStreamAsync("S2a-b", StreamState.NoStream, [Typed("UdiB", 3), Typed("UdiA", 4)]);
        var gotLive = await WaitUntil(() => Task.FromResult(received.Count >= 5), TimeSpan.FromSeconds(30));

        cts.Cancel();
        await pump;

        output.WriteLine($"SPIKE-2a after live-append (gotLive={gotLive}): " + string.Join(",", received));
        gotLive.Should().BeTrue("post-subscription appends must be PUSHED to the open index-stream subscription");
        received.Should().Equal(Enumerable.Range(0, 5),
            "the subscription must deliver history + live events in commit order 0..4");
    }

    // 2b: filtered SubscribeToAll over StreamFilter.Prefix($idx-user-<name>) — the subscription analog of the
    // filtered $all read ReadAsync already uses. This is the most likely real mechanism.
    [Fact]
    public async Task Spike2b_filtered_subscribe_to_all_catches_up_then_receives_live_appends_in_order()
    {
        await using var stores = new Store();
        var (client, conn) = await stores.NewAsync("s2b");

        await client.AppendToStreamAsync("S2b-a", StreamState.NoStream,
            [Typed("UdiA", 0), Typed("UdiB", 1), Typed("UdiA", 2)]);

        var src = new UserDefinedIndexSource(client, conn);
        var spec = new UserDefinedIndexSpec { Name = "s2b-merge", EventTypes = new HashSet<string> { "UdiA", "UdiB" } };
        await src.EnsureAsync(spec);
        await src.WaitUntilReadyAsync(spec.Name, expectedCount: 3, TimeSpan.FromSeconds(30));

        var indexStream = UserDefinedIndexSource.IndexStream(spec.Name);
        var received = new ConcurrentQueue<int>();
        using var cts = new CancellationTokenSource();
        string? subscribeError = null;

        var pump = Task.Run(async () =>
        {
            try
            {
                var filter = new SubscriptionFilterOptions(StreamFilter.Prefix(indexStream));
                await using var sub = client.SubscribeToAll(FromAll.Start, resolveLinkTos: true,
                    filterOptions: filter, cancellationToken: cts.Token);
                await foreach (var m in sub.Messages.WithCancellation(cts.Token))
                {
                    if (m is StreamMessage.Event(var e))
                    {
                        var ord = JsonNode.Parse(e.Event.Data.Span)?["Ord"]?.GetValue<int>() ?? -1;
                        received.Enqueue(ord);
                    }
                }
            }
            catch (OperationCanceledException) { /* expected on stop */ }
            catch (Exception ex) { subscribeError = $"{ex.GetType().Name}: {ex.Message}"; }
        });

        var caughtUp = await WaitUntil(() => Task.FromResult(received.Count >= 3), TimeSpan.FromSeconds(30));
        output.WriteLine($"SPIKE-2b subscribe error: {subscribeError ?? "<none>"}");
        output.WriteLine($"SPIKE-2b after catch-up (caughtUp={caughtUp}): " + string.Join(",", received));

        caughtUp.Should().BeTrue("filtered $all subscription must catch up over the index's history");

        await client.AppendToStreamAsync("S2b-b", StreamState.NoStream, [Typed("UdiB", 3), Typed("UdiA", 4)]);
        var gotLive = await WaitUntil(() => Task.FromResult(received.Count >= 5), TimeSpan.FromSeconds(30));

        cts.Cancel();
        await pump;

        output.WriteLine($"SPIKE-2b subscribe error (final): {subscribeError ?? "<none>"}");
        output.WriteLine($"SPIKE-2b after live-append (gotLive={gotLive}): " + string.Join(",", received));
        subscribeError.Should().BeNull("filtered $all subscription must not fault");
        gotLive.Should().BeTrue("post-subscription appends must be PUSHED to the open filtered-$all subscription");
        received.Should().Equal(Enumerable.Range(0, 5),
            "the filtered subscription must deliver history + live events in commit order 0..4");
    }

    // =====================================================================================================
    // SPIKE-3 — CHECKPOINT / RESUME. Record a position, stop, append more, resume from the recorded position,
    // and receive ONLY the new events (as a restarting read model must). Uses SubscribeToAll+filter and
    // resumes from the recorded $all Position (the mechanism that mirrors ReadAsync). If 2a works, the
    // index-stream revision path is also viable; this test uses the $all Position path.
    // =====================================================================================================
    [Fact]
    public async Task Spike3_can_record_position_stop_and_resume_receiving_only_new_events()
    {
        await using var stores = new Store();
        var (client, conn) = await stores.NewAsync("s3");

        await client.AppendToStreamAsync("S3-a", StreamState.NoStream,
            [Typed("UdiA", 0), Typed("UdiB", 1), Typed("UdiA", 2)]);

        var src = new UserDefinedIndexSource(client, conn);
        var spec = new UserDefinedIndexSpec { Name = "s3-merge", EventTypes = new HashSet<string> { "UdiA", "UdiB" } };
        await src.EnsureAsync(spec);
        await src.WaitUntilReadyAsync(spec.Name, expectedCount: 3, TimeSpan.FromSeconds(30));

        var indexStream = UserDefinedIndexSource.IndexStream(spec.Name);
        var filter = new SubscriptionFilterOptions(StreamFilter.Prefix(indexStream));

        // Pass 1: consume the 3 historical events, recording the LAST link's $all Position as the checkpoint.
        var firstPass = new List<int>();
        Position? checkpoint = null;
        using (var cts1 = new CancellationTokenSource())
        {
            var pump1 = Task.Run(async () =>
            {
                await using var sub = client.SubscribeToAll(FromAll.Start, resolveLinkTos: true,
                    filterOptions: filter, cancellationToken: cts1.Token);
                try
                {
                    await foreach (var m in sub.Messages.WithCancellation(cts1.Token))
                        if (m is StreamMessage.Event(var e))
                        {
                            firstPass.Add(JsonNode.Parse(e.Event.Data.Span)?["Ord"]?.GetValue<int>() ?? -1);
                            if (e.OriginalPosition is { } p) checkpoint = p; // link's $all position — the resume point
                        }
                }
                catch (OperationCanceledException) { }
            });
            await WaitUntil(() => Task.FromResult(firstPass.Count >= 3), TimeSpan.FromSeconds(30));
            cts1.Cancel();
            await pump1;
        }

        output.WriteLine($"SPIKE-3 pass1: {string.Join(",", firstPass)}  checkpoint={checkpoint?.ToString() ?? "<null>"}");
        firstPass.Should().Equal(new[] { 0, 1, 2 });
        checkpoint.Should().NotBeNull("a resumable $all position must be recoverable from the index link event");

        // Append MORE while "stopped".
        await client.AppendToStreamAsync("S3-b", StreamState.NoStream, [Typed("UdiB", 3), Typed("UdiA", 4)]);

        // Pass 2: RESUME from the recorded checkpoint (FromAll.After) — must receive ONLY 3,4 (not 0,1,2 again).
        var secondPass = new ConcurrentQueue<int>();
        using (var cts2 = new CancellationTokenSource())
        {
            var pump2 = Task.Run(async () =>
            {
                await using var sub = client.SubscribeToAll(FromAll.After(checkpoint!.Value), resolveLinkTos: true,
                    filterOptions: filter, cancellationToken: cts2.Token);
                try
                {
                    await foreach (var m in sub.Messages.WithCancellation(cts2.Token))
                        if (m is StreamMessage.Event(var e))
                            secondPass.Enqueue(JsonNode.Parse(e.Event.Data.Span)?["Ord"]?.GetValue<int>() ?? -1);
                }
                catch (OperationCanceledException) { }
            });
            await WaitUntil(() => Task.FromResult(secondPass.Count >= 2), TimeSpan.FromSeconds(30));
            cts2.Cancel();
            await pump2;
        }

        output.WriteLine($"SPIKE-3 pass2 (resumed): {string.Join(",", secondPass)}");
        secondPass.Should().Equal(new[] { 3, 4 },
            "resuming from the recorded position must yield ONLY events after it, in commit order — no replay of 0,1,2");
    }

    // =====================================================================================================
    // SPIKE-4 — BODY-PROPERTY ROUTING (the likely hard limit). The current lookup projection does
    // linkTo('cat-' + e.body.SomeId): ONE stream PER distinct SomeId value. Empirically probe whether a
    // user-defined index filter can (a) reference the event BODY at all, and note that even if it can, an
    // index still produces ONE merged stream — it cannot PARTITION into N per-value streams.
    // =====================================================================================================
    [Fact]
    public async Task Spike4_body_property_routing_is_not_expressible_as_a_user_defined_index()
    {
        await using var stores = new Store();
        var (client, conn) = await stores.NewAsync("s4");

        // Two SomeId partitions x,y interleaved. A lookup projection would route these to cat-x and cat-y.
        await client.AppendToStreamAsync("S4-a", StreamState.NoStream, new[]
        {
            Bodied("UdiC", 0, "x"), Bodied("UdiC", 1, "y"), Bodied("UdiC", 2, "x"), Bodied("UdiC", 3, "y")
        });

        // Probe: try to POST a user-defined index whose filter references the event BODY (rec.data / rec.body).
        // We hit the same /v2/indexes endpoint EnsureAsync uses, but with a body-referencing filter, and capture
        // the raw HTTP outcome + whether it indexes the expected subset.
        var (httpBase, user, pass) = KurrentHttpEndpoint.Parse(conn);
        using var http = KurrentHttpEndpoint.CreateClient(user, pass);

        var probes = new[]
        {
            ("data", "rec => rec.data.SomeId == \"x\""),
            ("body", "rec => rec.body.SomeId == \"x\""),
            ("dataloweronly", "rec => rec.data.someid == \"x\""),
            // Baseline control: a SCHEMA-name filter with the SAME lowercase name MUST be accepted — proves any
            // 400 on the body probes above is about the FILTER, not the (now-valid) index name.
            ("schemacontrol", "rec => rec.schema.name == \"UdiC\""),
        };
        for (var i = 0; i < probes.Length; i++)
        {
            var (label, filterExpr) = probes[i];
            var name = $"s4-{label}"; // lowercase alnum + dash only — a valid index name
            var url = new Uri(httpBase, $"v2/indexes/{Uri.EscapeDataString(name)}");
            var payload = new JsonObject { ["filter"] = filterExpr, ["start"] = true };
            using var content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
            var resp = await http.PostAsync(url, content);
            var body = await resp.Content.ReadAsStringAsync();
            output.WriteLine($"SPIKE-4 POST filter [{filterExpr}] -> HTTP {(int)resp.StatusCode} {resp.StatusCode}: {body}");

            if (resp.IsSuccessStatusCode)
            {
                // If accepted, check whether it actually indexed only the x-partition (2 events) — i.e. whether the
                // engine evaluated the body predicate. Read via filtered $all.
                var idxSrc = new UserDefinedIndexSource(client, conn);
                await Task.Delay(1500); // let it backfill
                var ords = new List<int>();
                try { ords = await ReadOrdsAsync(idxSrc, name); } catch (Exception ex) { output.WriteLine($"  read err: {ex.Message}"); }
                output.WriteLine($"  indexed ords for [{filterExpr}]: {string.Join(",", ords)} (x-partition would be 0,2)");
            }
        }

        // The structural point, independent of whether body filtering is accepted: a user-defined index is a
        // FILTER producing ONE $idx-user-<name> stream. It has NO 'linkTo(dynamic-name)' — it cannot emit N
        // streams keyed by a body value. So the lookup/by-key projection (cat-<SomeId>) is NOT replaceable by an
        // index; this assertion documents the empirical conclusion (evidence is in the logged POST outcomes).
        output.WriteLine("SPIKE-4 conclusion: a user-defined index cannot PARTITION by body value (no dynamic linkTo); " +
                         "at best it filters to one merged stream. Lookup-by-key projections are NOT index-replaceable.");
        true.Should().BeTrue();
    }

    // =====================================================================================================
    // SPIKE-5 (arch §3 Q4) — FILTER DRIFT / RECREATION COST. Can an existing index's filter be mutated in
    // place? If not, is there a DELETE, and what does delete+recreate+backfill cost for a realistic size?
    // This becomes the "projection update" replacement for mp_query_hash's disable/update/enable.
    // =====================================================================================================
    [Fact]
    public async Task Spike5_filter_drift_and_recreation_cost()
    {
        await using var stores = new Store();
        var (client, conn) = await stores.NewAsync("s5");

        // A realistic-size store: 5000 UdiA + 5000 UdiB interleaved (10k total).
        const int per = 5000;
        var expected = StreamState.NoStream;
        var total = 0;
        for (var offset = 0; offset < per * 2; offset += 1000)
        {
            var chunk = Enumerable.Range(offset, 1000).Select(i => Typed(i % 2 == 0 ? "UdiA" : "UdiB", i)).ToArray();
            await client.AppendToStreamAsync("S5-1", expected, chunk);
            expected = (ulong)(offset + chunk.Length - 1);
            total += chunk.Length;
        }

        var (httpBase, user, pass) = KurrentHttpEndpoint.Parse(conn);
        using var http = KurrentHttpEndpoint.CreateClient(user, pass);
        var src = new UserDefinedIndexSource(client, conn);

        // Create index filtering ONLY UdiA (5000 events).
        var name = "s5-drift";
        var url = new Uri(httpBase, $"v2/indexes/{name}");
        async Task<(HttpStatusCode, string)> Post(string filter)
        {
            using var c = new StringContent(new JsonObject { ["filter"] = filter, ["start"] = true }.ToJsonString(),
                Encoding.UTF8, "application/json");
            var r = await http.PostAsync(url, c);
            return (r.StatusCode, await r.Content.ReadAsStringAsync());
        }
        async Task<string?> GetFilter()
        {
            var r = await http.GetAsync(url);
            if (!r.IsSuccessStatusCode) return $"<GET {(int)r.StatusCode}>";
            return JsonNode.Parse(await r.Content.ReadAsStringAsync())?["index"]?["filter"]?.GetValue<string>();
        }

        var onlyA = "rec => rec.schema.name == \"UdiA\"";
        var aAndB = "rec => rec.schema.name == \"UdiA\" || rec.schema.name == \"UdiB\"";

        var (createStatus, createBody) = await Post(onlyA);
        output.WriteLine($"SPIKE-5 create(onlyA) -> HTTP {(int)createStatus}: {createBody}");
        await src.WaitUntilReadyAsync(name, expectedCount: per, TimeSpan.FromSeconds(120));
        output.WriteLine($"SPIKE-5 filter after create: {await GetFilter()}");

        // (a) Attempt in-place mutation: POST same name, DIFFERENT filter (A+B).
        var (mutStatus, mutBody) = await Post(aAndB);
        var filterAfterMutate = await GetFilter();
        output.WriteLine($"SPIKE-5 mutate(A+B) POST -> HTTP {(int)mutStatus}: {mutBody}");
        output.WriteLine($"SPIKE-5 filter after mutate attempt: {filterAfterMutate}  (unchanged={filterAfterMutate == onlyA})");

        // (b) Is there a DELETE? Capture verbatim.
        var delResp = await http.DeleteAsync(url);
        var delBody = await delResp.Content.ReadAsStringAsync();
        output.WriteLine($"SPIKE-5 DELETE -> HTTP {(int)delResp.StatusCode} {delResp.StatusCode}: {delBody}");

        // (c) If delete worked, measure delete+recreate+backfill cost for the WIDENED filter (A+B = 10k).
        if (delResp.IsSuccessStatusCode)
        {
            // small settle so the delete is visible
            await Task.Delay(500);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var (recStatus, recBody) = await Post(aAndB);
            output.WriteLine($"SPIKE-5 recreate(A+B) -> HTTP {(int)recStatus}: {recBody}");
            try
            {
                await src.WaitUntilReadyAsync(name, expectedCount: total, TimeSpan.FromSeconds(180));
                sw.Stop();
                output.WriteLine($"SPIKE-5 recreate+backfill of {total} events took {sw.ElapsedMilliseconds} ms");
                var widened = await GetFilter();
                output.WriteLine($"SPIKE-5 filter after recreate: {widened}");
            }
            catch (Exception ex)
            {
                sw.Stop();
                output.WriteLine($"SPIKE-5 recreate backfill FAILED after {sw.ElapsedMilliseconds} ms: {ex.Message}");
            }
        }
        else
        {
            output.WriteLine("SPIKE-5: no working DELETE — an index cannot be cheaply re-pointed; a filter change " +
                             "requires a NEW index name (old one lingers). This is the update-cost finding.");
        }

        // No hard assert on the timing (informational), but the in-place mutation MUST NOT silently change the
        // filter — that's the correctness-relevant part (drift is either rejected or ignored, never a silent swap).
        // If the server DID redefine in place, filterAfterMutate would equal aAndB; record whichever happened.
        (filterAfterMutate == onlyA || filterAfterMutate == aAndB || filterAfterMutate!.StartsWith("<GET"))
            .Should().BeTrue("captured the actual post-mutation filter state for the report");
    }

    // =====================================================================================================
    // SPIKE-6 (arch §3 Q6) — INDEX COUNT CEILING. 20 read models exist today under a naive 1:1 index-per-model
    // mapping and that grows. Create ~60 indexes on one node; confirm no rejection/degradation and that a
    // sampled index still reads correctly.
    // =====================================================================================================
    [Fact]
    public async Task Spike6_many_indexes_no_rejection_or_degradation()
    {
        await using var stores = new Store();
        var (client, conn) = await stores.NewAsync("s6");

        // A modest shared store; every index filters the same two types (worst case: all overlap $all fully).
        await client.AppendToStreamAsync("S6-1", StreamState.NoStream,
            Enumerable.Range(0, 100).Select(i => Typed(i % 2 == 0 ? "UdiA" : "UdiB", i)).ToArray());

        var src = new UserDefinedIndexSource(client, conn);
        const int count = 60;
        var failures = new List<string>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < count; i++)
        {
            try
            {
                await src.EnsureAsync(new UserDefinedIndexSpec
                {
                    Name = $"s6-idx-{i:D3}",
                    EventTypes = new HashSet<string> { "UdiA", "UdiB" }
                });
            }
            catch (Exception ex) { failures.Add($"#{i}: {ex.GetType().Name} {ex.Message}"); }
        }
        sw.Stop();
        output.WriteLine($"SPIKE-6 created {count - failures.Count}/{count} indexes in {sw.ElapsedMilliseconds} ms; " +
                         $"failures={failures.Count}");
        foreach (var f in failures.Take(5)) output.WriteLine("  " + f);

        // Sample the FIRST and LAST index: both must backfill to the full 100 and read in commit order — proving
        // no degradation/starvation under many concurrent index backfills.
        foreach (var idx in new[] { "s6-idx-000", $"s6-idx-{count - 1:D3}" })
        {
            await src.WaitUntilReadyAsync(idx, expectedCount: 100, TimeSpan.FromSeconds(120));
            var ords = await ReadOrdsAsync(src, idx);
            output.WriteLine($"SPIKE-6 sample {idx}: count={ords.Count}, ordered={ords.SequenceEqual(Enumerable.Range(0, 100))}");
            ords.Should().Equal(Enumerable.Range(0, 100), $"{idx} must read the full merge in commit order");
        }

        failures.Should().BeEmpty("creating ~60 indexes on one node must not be rejected");
    }

    // =====================================================================================================
    // SPIKE-7 (team-lead's "SPIKE-5 persistent" / arch's persistent-sub open item) — THE REAL BLOCKER for
    // shape-A merges that go through SubscribeEventHandlerPersistently (ProcessManager Inbox/Outbox, competing
    // consumers, server checkpoints). EMPIRICAL RESULT: a SERVER-CHECKPOINTED PERSISTENT subscription over
    // filtered $all (StreamFilter.Prefix("$idx-user-<name>") + resolveLinkTos) is CREATABLE and connects with
    // NO error — but delivers ZERO index entries (catch-up AND live). Same silent-empty as SubscribeToStream on
    // the index stream (SPIKE-2a). The Spike7_controls test proves this is NOT a broken harness (a persistent
    // filtered-$all sub over a NON-link filter delivers fine) — persistent subscriptions simply do not see the
    // $idx-user-* index links, by ANY mechanism (filtered-$all resolve/no-resolve, or direct CreateToStream).
    // This test ENCODES that measured reality: create succeeds, subscribe yields nothing. Contrast SPIKE-2b
    // (catch-up SubscribeToAll+filter) which DOES deliver — so index-backing is a CATCH-UP-only capability.
    // =====================================================================================================
    [Fact]
    public async Task Spike7_persistent_subscription_over_filtered_all_delivers_nothing_blocker()
    {
        await using var stores = new Store();
        var (client, ps, conn) = await stores.NewWithPsAsync("s7");

        await client.AppendToStreamAsync("S7-a", StreamState.NoStream,
            [Typed("UdiA", 0), Typed("UdiB", 1), Typed("UdiA", 2)]);

        var src = new UserDefinedIndexSource(client, conn);
        var spec = new UserDefinedIndexSpec { Name = "s7-merge", EventTypes = new HashSet<string> { "UdiA", "UdiB" } };
        await src.EnsureAsync(spec);
        await src.WaitUntilReadyAsync(spec.Name, expectedCount: 3, TimeSpan.FromSeconds(30));

        var indexStream = UserDefinedIndexSource.IndexStream(spec.Name);
        const string group = "rm-group";

        // The persistent group over FILTERED $all with resolveLinkTos IS creatable (this is the trap — it looks
        // like it should work).
        string? createError = null;
        try
        {
            await ps.CreateToAllAsync(group, StreamFilter.Prefix(indexStream),
                new PersistentSubscriptionSettings(resolveLinkTos: true, startFrom: Position.Start,
                    checkPointLowerBound: 1, checkPointAfter: TimeSpan.FromMilliseconds(100)));
        }
        catch (Exception ex) { createError = $"{ex.GetType().Name}: {ex.Message}"; }
        output.WriteLine($"SPIKE-7 CreateToAllAsync(filtered) error: {createError ?? "<none>"}");
        createError.Should().BeNull("the persistent group IS creatable — the failure is delivery, not creation");

        var received = new ConcurrentQueue<int>();
        string? pumpError = null;
        using var cts = new CancellationTokenSource();
        var pump = Task.Run(async () =>
        {
            try
            {
                await using var sub = ps.SubscribeToAll(group, cancellationToken: cts.Token);
                await foreach (var e in sub.WithCancellation(cts.Token))
                {
                    received.Enqueue(JsonNode.Parse(e.Event.Data.Span)?["Ord"]?.GetValue<int>() ?? -1);
                    await sub.Ack(e);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { pumpError = $"{ex.GetType().Name}: {ex.Message}"; }
        });

        // Wait for catch-up that will NOT come; also append live to prove live push is dead too.
        var caughtUp = await WaitUntil(() => Task.FromResult(received.Count >= 3), TimeSpan.FromSeconds(15));
        await client.AppendToStreamAsync("S7-b", StreamState.NoStream, [Typed("UdiB", 3), Typed("UdiA", 4)]);
        var gotLive = await WaitUntil(() => Task.FromResult(received.Count >= 5), TimeSpan.FromSeconds(15));

        cts.Cancel();
        await pump;

        output.WriteLine($"SPIKE-7 pump error: {pumpError ?? "<none>"}");
        output.WriteLine($"SPIKE-7 persistent delivery — caughtUp={caughtUp} gotLive={gotLive} received=[{string.Join(",", received)}]");
        pumpError.Should().BeNull("subscribing does not fault — it just silently delivers nothing");
        received.Should().BeEmpty(
            "MEASURED BLOCKER: a persistent subscription over filtered $all delivers ZERO $idx-user-* index entries " +
            "(catch-up and live) — no error, no events. Persistent (server-checkpointed) read models CANNOT be " +
            "index-backed; only catch-up (client-checkpointed) subscriptions see the index (SPIKE-2b). See " +
            "Spike7_controls for the harness-sanity control (a NON-link filter delivers fine).");
    }

    // SPIKE-7 CONTROLS — distinguish "my persistent-sub harness is broken" from "persistent-sub-to-$idx-user
    // genuinely delivers nothing". Counts what a filtered persistent $all subscription delivers for:
    //  A) a NORMAL event-type filter (domain events directly in $all)      -> proves the harness works at all
    //  B) StreamFilter.Prefix($idx-user-*) with resolveLinkTos:FALSE       -> are the raw index LINK events deliverable
    //  C) StreamFilter.Prefix($idx-user-*) with resolveLinkTos:TRUE        -> the failing case, re-measured alongside
    private async Task<int> DrainPersistentAsync(KurrentDBPersistentSubscriptionsClient ps, string group,
        int expect, TimeSpan timeout)
    {
        var got = new ConcurrentQueue<int>();
        using var cts = new CancellationTokenSource();
        var pump = Task.Run(async () =>
        {
            try
            {
                await using var sub = ps.SubscribeToAll(group, cancellationToken: cts.Token);
                await foreach (var e in sub.WithCancellation(cts.Token))
                {
                    got.Enqueue(1);
                    await sub.Ack(e);
                }
            }
            catch (OperationCanceledException) { }
        });
        await WaitUntil(() => Task.FromResult(got.Count >= expect), timeout);
        await Task.Delay(300);
        cts.Cancel();
        await pump;
        return got.Count;
    }

    [Fact]
    public async Task Spike7_controls_isolate_whether_persistent_filtered_all_harness_works()
    {
        await using var stores = new Store();
        var (client, ps, conn) = await stores.NewWithPsAsync("s7c");

        await client.AppendToStreamAsync("S7c-a", StreamState.NoStream,
            [Typed("UdiA", 0), Typed("UdiB", 1), Typed("UdiA", 2)]);

        var src = new UserDefinedIndexSource(client, conn);
        var spec = new UserDefinedIndexSpec { Name = "s7c-merge", EventTypes = new HashSet<string> { "UdiA", "UdiB" } };
        await src.EnsureAsync(spec);
        await src.WaitUntilReadyAsync(spec.Name, expectedCount: 3, TimeSpan.FromSeconds(30));
        var indexStream = UserDefinedIndexSource.IndexStream(spec.Name);

        var settings = () => new PersistentSubscriptionSettings(resolveLinkTos: false, startFrom: Position.Start,
            checkPointLowerBound: 1, checkPointAfter: TimeSpan.FromMilliseconds(100));

        // A) Normal event-type filter over $all (domain events themselves). Proves the harness delivers.
        await ps.CreateToAllAsync("ctrl-a", EventTypeFilter.Prefix("Udi"), settings());
        var a = await DrainPersistentAsync(ps, "ctrl-a", 3, TimeSpan.FromSeconds(20));
        output.WriteLine($"SPIKE-7 CONTROL A (EventTypeFilter 'Udi', domain events): delivered {a} (expect 3)");

        // B) $idx-user prefix filter, resolveLinkTos FALSE — raw link events.
        await ps.CreateToAllAsync("ctrl-b", StreamFilter.Prefix(indexStream), settings());
        var b = await DrainPersistentAsync(ps, "ctrl-b", 3, TimeSpan.FromSeconds(20));
        output.WriteLine($"SPIKE-7 CONTROL B ($idx-user prefix, resolveLinkTos=FALSE): delivered {b} (expect 3 if links deliverable)");

        // C) $idx-user prefix filter, resolveLinkTos TRUE — the failing case, measured here too.
        await ps.CreateToAllAsync("ctrl-c", StreamFilter.Prefix(indexStream),
            new PersistentSubscriptionSettings(resolveLinkTos: true, startFrom: Position.Start,
                checkPointLowerBound: 1, checkPointAfter: TimeSpan.FromMilliseconds(100)));
        var c = await DrainPersistentAsync(ps, "ctrl-c", 3, TimeSpan.FromSeconds(20));
        output.WriteLine($"SPIKE-7 CONTROL C ($idx-user prefix, resolveLinkTos=TRUE): delivered {c} (expect 3)");

        // D) PERSISTENT subscription created DIRECTLY on the $idx-user stream (CreateToStreamAsync) — the
        // persistent analog of SPIKE-2a's SubscribeToStream, measured rather than inferred.
        var d = -1; string? dErr = null;
        try
        {
            await ps.CreateToStreamAsync(indexStream, "ctrl-d",
                new PersistentSubscriptionSettings(resolveLinkTos: true, startFrom: StreamPosition.Start,
                    checkPointLowerBound: 1, checkPointAfter: TimeSpan.FromMilliseconds(100)));
            var got = new ConcurrentQueue<int>();
            using var cts = new CancellationTokenSource();
            var pump = Task.Run(async () =>
            {
                try
                {
                    await using var sub = ps.SubscribeToStream(indexStream, "ctrl-d", cancellationToken: cts.Token);
                    await foreach (var e in sub.WithCancellation(cts.Token)) { got.Enqueue(1); await sub.Ack(e); }
                }
                catch (OperationCanceledException) { }
            });
            await WaitUntil(() => Task.FromResult(got.Count >= 3), TimeSpan.FromSeconds(20));
            await Task.Delay(300); cts.Cancel(); await pump;
            d = got.Count;
        }
        catch (Exception ex) { dErr = $"{ex.GetType().Name}: {ex.Message}"; }
        output.WriteLine($"SPIKE-7 CONTROL D (persistent CreateToStream on $idx-user): delivered {d} (expect 3), err={dErr ?? "<none>"}");

        output.WriteLine($"SPIKE-7 CONTROLS SUMMARY: A(domain)={a} B(link,noresolve)={b} C(link,resolve)={c} D(persist-to-stream)={d}");
        // A is the harness sanity check — it MUST deliver. B/C/D are the actual findings (logged, not asserted hard).
        a.Should().Be(3, "the persistent filtered-$all harness itself works when the filter matches non-link events");
    }
}
