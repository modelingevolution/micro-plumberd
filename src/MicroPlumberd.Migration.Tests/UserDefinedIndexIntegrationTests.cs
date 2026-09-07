using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using KurrentDB.Client;
using MicroPlumberd.Migration;
using MicroPlumberd.Testing;
using MicroPlumberd.Tests.App.Domain;
using Xunit;
using Xunit.Abstractions;

namespace MicroPlumberd.Migration.Tests;

/// <summary>
/// Integration proof of the CLEAN merge read-path against a REAL KurrentDB 26.1 (user-defined indexes), the
/// alternative to the paced <see cref="ProjectionCopier"/>. Uses the same <see cref="EventStoreServer"/> Docker
/// fixture as the other migration tests (NEVER Testcontainers).
///
/// Covers: (a) create + ready-poll (the STARTED-immediately / backfill-async gotcha is handled);
/// (b) an interleaved multi-type merge reads in COMMIT order (0,1,2,3,4), NOT the type-clustered 0,2,4,1,3 that
/// a <c>fromStreams</c> join produces (proven in <see cref="ProjectionBehaviorSpikes"/>); (c) idempotent
/// re-create; (d) a full migration with a <see cref="UserDefinedIndexCopyContext"/> copies the aggregate
/// streams faithfully AND its dest index reproduces the SAME merged order the <see cref="ProjectionCopyContext"/>
/// path builds into the physical merge stream.
/// </summary>
[Trait("Category", "Integration")]
public class UserDefinedIndexIntegrationTests(ITestOutputHelper output)
{
    private sealed class Store : IAsyncDisposable
    {
        private readonly List<EventStoreServer> _servers = new();

        public async Task<(KurrentDBClient Client, KurrentDBProjectionManagementClient Projections, string Conn, IPlumber Plumber)>
            NewAsync(string tag)
        {
            var es = EventStoreServer.Create($"mp-udix-{tag}-{Guid.NewGuid():N}");
            _servers.Add(es);
            await es.StartInDocker(inMemory: true);
            var s = es.GetEventStoreSettings();
            return (new KurrentDBClient(s), new KurrentDBProjectionManagementClient(s), es.HttpUrl.ToString(),
                global::MicroPlumberd.Plumber.Create(s));
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var s in _servers) await s.DisposeAsync();
        }
    }

    private static EventData Typed(string type, int ord) =>
        new(Uuid.NewUuid(), type, Encoding.UTF8.GetBytes($"{{\"Ord\":{ord}}}"));

    private static async Task<List<int>> ReadOrdsAsync(UserDefinedIndexSource src, string name, CancellationToken ct = default)
    {
        var ords = new List<int>();
        await foreach (var e in src.ReadAsync(name, ct))
            ords.Add(e.Data?["Ord"]?.GetValue<int>() ?? -1);
        return ords;
    }

    // (a) + (b): interleaved arrival across two event types must read back in COMMIT order via the index — the
    // core property. Also exercises create + WaitUntilReady (handles STARTED-immediately + async backfill).
    [Fact]
    public async Task Index_reads_interleaved_multitype_merge_in_commit_order_not_type_clustered()
    {
        await using var stores = new Store();
        var (client, _, conn, _) = await stores.NewAsync("order");

        // A0, B1, A2, B3, A4 — a fromStreams join would type-cluster this to 0,2,4,1,3 (ProjectionBehaviorSpikes).
        await client.AppendToStreamAsync("Spike-1", StreamState.NoStream,
            [Typed("UdiA", 0), Typed("UdiB", 1), Typed("UdiA", 2), Typed("UdiB", 3), Typed("UdiA", 4)]);

        var src = new UserDefinedIndexSource(client, conn);
        var spec = new UserDefinedIndexSpec
        {
            Name = "merge-ab",
            EventTypes = new HashSet<string> { "UdiA", "UdiB" }
        };

        await src.EnsureAsync(spec);
        await src.WaitUntilReadyAsync(spec.Name, expectedCount: 5, TimeSpan.FromSeconds(30));
        var ords = await ReadOrdsAsync(src, spec.Name);

        output.WriteLine("index Ord order: " + string.Join(",", ords));
        ords.Should().Equal(new[] { 0, 1, 2, 3, 4 },
            "the user-defined index is read via filtered $all → commit order, NOT the type-clustered 0,2,4,1,3");
        ords.Should().NotEqual(new[] { 0, 2, 4, 1, 3 }, "commit order must not degrade to type-clustered");
    }

    // (a) explicit: ready-polling completes and the read is COMPLETE (all backfilled) even when called
    // immediately after creation (the index reports STARTED before it has finished backfilling history).
    [Fact]
    public async Task Create_then_immediately_wait_ready_yields_complete_backfilled_read()
    {
        await using var stores = new Store();
        var (client, _, conn, _) = await stores.NewAsync("ready");

        const int n = 50;
        var batch = Enumerable.Range(0, n).Select(i => Typed(i % 2 == 0 ? "UdiA" : "UdiB", i)).ToArray();
        await client.AppendToStreamAsync("Big-1", StreamState.NoStream, batch);

        var src = new UserDefinedIndexSource(client, conn);
        var spec = new UserDefinedIndexSpec
        {
            Name = "readytest",
            EventTypes = new HashSet<string> { "UdiA", "UdiB" }
        };

        // Ensure + wait-ready back-to-back: WaitUntilReadyAsync must not return before the backfill is complete.
        // The expected count is computed AUTHORITATIVELY from the store's own $all (not hard-coded), then required.
        await src.EnsureAsync(spec);
        var expected = await src.CountMatchingAsync(spec.EventTypes);
        expected.Should().Be(n, "CountMatchingAsync reads the exact number of matching events from $all");
        await src.WaitUntilReadyAsync(spec.Name, expected, TimeSpan.FromSeconds(30));

        var ords = await ReadOrdsAsync(src, spec.Name);
        ords.Should().HaveCount(n, "ready-poll must wait for the full backfill, not return at STARTED");
        ords.Should().Equal(Enumerable.Range(0, n), "and the backfilled read is in commit order");
    }

    // (c): re-creating the same index (same name + filter) is a no-op and does not disturb the read.
    [Fact]
    public async Task Ensure_is_idempotent_on_recreate()
    {
        await using var stores = new Store();
        var (client, _, conn, _) = await stores.NewAsync("idem");
        await client.AppendToStreamAsync("S-1", StreamState.NoStream, [Typed("UdiA", 0), Typed("UdiB", 1)]);

        var src = new UserDefinedIndexSource(client, conn);
        var spec = new UserDefinedIndexSpec
        {
            Name = "idempotent",
            EventTypes = new HashSet<string> { "UdiA", "UdiB" }
        };

        await src.EnsureAsync(spec);
        await src.WaitUntilReadyAsync(spec.Name, expectedCount: 2, TimeSpan.FromSeconds(30));

        // Second Ensure with the identical spec must NOT throw (409 already-exists is treated as success).
        var act = async () => await src.EnsureAsync(spec);
        await act.Should().NotThrowAsync();

        var ords = await ReadOrdsAsync(src, spec.Name);
        ords.Should().Equal(new[] { 0, 1 }, "the index is unchanged after an idempotent re-create");
    }

    // PROVING the readiness is AUTHORITATIVE, not a stability heuristic: it blocks until the indexed count
    // equals the KNOWN total and FAILS LOUD (TimeoutException) if that total is never reached — a heuristic
    // would instead have "settled" on whatever count two consecutive polls happened to agree on.
    [Fact]
    public async Task WaitUntilReady_is_authoritative_waits_for_exact_count_and_times_out_if_short()
    {
        await using var stores = new Store();
        var (client, _, conn, _) = await stores.NewAsync("auth");

        const int n = 40;
        var batch = Enumerable.Range(0, n).Select(i => Typed(i % 2 == 0 ? "UdiA" : "UdiB", i)).ToArray();
        await client.AppendToStreamAsync("Auth-1", StreamState.NoStream, batch);

        var src = new UserDefinedIndexSource(client, conn);
        var spec = new UserDefinedIndexSpec
        {
            Name = "authoritative",
            EventTypes = new HashSet<string> { "UdiA", "UdiB" }
        };
        await src.EnsureAsync(spec);

        // The authoritative expected count comes from the store's own $all — exactly n.
        (await src.CountMatchingAsync(spec.EventTypes)).Should().Be(n);

        // Waiting for the REAL total succeeds and yields exactly n, in commit order.
        await src.WaitUntilReadyAsync(spec.Name, expectedCount: n, TimeSpan.FromSeconds(30));
        (await ReadOrdsAsync(src, spec.Name)).Should().Equal(Enumerable.Range(0, n));

        // Waiting for MORE than exist can NEVER be satisfied → it times out rather than falsely reporting ready.
        // (A stability heuristic would have returned at n; the authoritative wait refuses to.)
        var act = async () => await src.WaitUntilReadyAsync(spec.Name, expectedCount: n + 1, TimeSpan.FromSeconds(4));
        (await act.Should().ThrowAsync<TimeoutException>())
            .Which.Message.Should().Contain($"{n + 1}", "the timeout must report the unreachable target count");
    }

    // PROVING no truncation on a LARGE merge that spans many backfill/poll intervals: a heuristic gate ("two
    // equal counts 100ms apart") could false-ready on a mid-backfill lull and yield a truncated tail; the
    // authoritative expected-count gate must yield ALL n, in commit order, no matter how the backfill paces.
    // n is large enough that the backfill is NOT instantaneous (thousands of links across many $et catch-ups).
    [Fact]
    public async Task Large_merge_reads_back_full_count_in_commit_order_no_truncation()
    {
        await using var stores = new Store();
        var (client, _, conn, _) = await stores.NewAsync("large");

        const int n = 20_000;
        // Interleaved across two event types so a fromStreams join would type-cluster; appended in chunks under
        // KurrentDB's per-append cap, preserving global Ord order 0..n-1 in $all commit order.
        var expected = StreamState.NoStream;
        for (var offset = 0; offset < n; offset += 1000)
        {
            var chunk = Enumerable.Range(offset, Math.Min(1000, n - offset))
                .Select(i => Typed(i % 2 == 0 ? "UdiA" : "UdiB", i)).ToArray();
            await client.AppendToStreamAsync("Large-1", expected, chunk);
            expected = (ulong)(offset + chunk.Length - 1);
        }

        var src = new UserDefinedIndexSource(client, conn);
        var spec = new UserDefinedIndexSpec { Name = "large-merge", EventTypes = new HashSet<string> { "UdiA", "UdiB" } };
        await src.EnsureAsync(spec);

        var total = await src.CountMatchingAsync(spec.EventTypes);
        total.Should().Be(n, "the authoritative expected count is the exact number of matching events in $all");

        await src.WaitUntilReadyAsync(spec.Name, total, TimeSpan.FromSeconds(120));

        // The whole merge reads back — full count AND commit order 0..n-1, i.e. NOT a truncated tail and NOT
        // type-clustered. This is the property the expected-count gate guarantees regardless of backfill pacing.
        var ords = await ReadOrdsAsync(src, spec.Name);
        ords.Should().HaveCount(n, "the reader must yield every indexed event, never a truncated tail");
        ords.Should().Equal(Enumerable.Range(0, n), "in true commit order, not type-clustered");
    }

    // (d): a full migration using the index copy context — aggregate streams copied faithfully (verification OK),
    // and the DEST index reproduces the SAME merged commit order the projection-copy path builds into
    // FooModel_v1. Cross-checks the two merge-recovery strategies against ONE source.
    [Fact]
    public async Task Index_copy_matches_projection_copy_merged_order_and_faithful_aggregate_copy()
    {
        await using var stores = new Store();
        var (srcClient, srcProj, srcConn, srcPlumber) = await stores.NewAsync("dsrc");

        // Interleaved arrival across two aggregates: FooCreated(a), FooRefined(a), FooCreated(b), FooRefined(b).
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await srcPlumber.SaveNew(FooAggregate.Open("a-created", a));
        var aggA = await srcPlumber.Get<FooAggregate>(a);
        aggA.Refine("a-refined"); await srcPlumber.SaveChanges(aggA);
        await srcPlumber.SaveNew(FooAggregate.Open("b-created", b));
        var aggB = await srcPlumber.Get<FooAggregate>(b);
        aggB.Refine("b-refined"); await srcPlumber.SaveChanges(aggB);

        // Build the app's [OutputStream] join projection on source so the index copy can DISCOVER it.
        await srcPlumber.TryCreateJoinProjection<FooModel>();
        await WaitUntil(async () => await CountStreamAsync(srcClient, "FooModel_v1") >= 4, TimeSpan.FromSeconds(30));

        var expected = new[] { "a-created", "a-refined", "b-created", "b-refined" };

        // --- INDEX COPY path ---
        var (idxClient, idxProj, idxConn, _) = await stores.NewAsync("didx");
        var indexResult = await new MigrationRunner().RunAsync(srcClient, idxClient, Array.Empty<Migration>(),
            dryRun: false, projectionCopy: null,
            indexCopy: new UserDefinedIndexCopyContext
            {
                SourceProjections = srcProj,
                SourceConnectionString = srcConn,
                DestConnectionString = idxConn,
                ReadyTimeout = TimeSpan.FromSeconds(30)
            });

        indexResult.Verification!.AllOk.Should().BeTrue(indexResult.Verification.Format());
        var created = indexResult.CreatedIndexes.Should().ContainSingle(i => i.OutputStream == "FooModel_v1").Subject;
        created.EventTypes.Should().BeEquivalentTo(new[] { "FooCreated", "FooRefined" });

        var idxSource = new UserDefinedIndexSource(idxClient, idxConn);
        var names = new List<string?>();
        await foreach (var e in idxSource.ReadAsync(created.IndexName))
            names.Add(e.Data?["Name"]?.GetValue<string>());
        output.WriteLine("index merged order: " + string.Join(",", names));
        names.Should().Equal(expected, "the index reproduces the merge in commit order, not type-clustered");

        // --- PROJECTION COPY path (cross-check) — same source → the physical FooModel_v1 must match ---
        var (projClient, projProj, _, _) = await stores.NewAsync("dproj");
        await new MigrationRunner().RunAsync(srcClient, projClient, Array.Empty<Migration>(), dryRun: false,
            projectionCopy: new ProjectionCopyContext
            {
                SourceProjections = srcProj,
                DestProjections = projProj,
                SourceConnectionString = srcConn,
                PerEventPaceTimeout = TimeSpan.FromSeconds(30),
                DrainTimeout = TimeSpan.FromSeconds(60)
            });
        await WaitUntil(async () => await CountStreamAsync(projClient, "FooModel_v1") >= 4, TimeSpan.FromSeconds(30));
        var projNames = await ReadJoinNamesAsync(projClient, "FooModel_v1");
        projNames.Should().Equal(expected);

        // The two strategies agree on the merged order — the index is a faithful alternative to the paced copy.
        names.Should().Equal(projNames, "index-copy and projection-copy must produce the same merged order");
    }

    private static async Task<long> CountStreamAsync(KurrentDBClient c, string stream)
    {
        var res = c.ReadStreamAsync(Direction.Forwards, stream, StreamPosition.Start, resolveLinkTos: false);
        if (await res.ReadState == ReadState.StreamNotFound) return 0;
        return await res.LongCountAsync();
    }

    private static async Task<List<string?>> ReadJoinNamesAsync(KurrentDBClient c, string stream)
    {
        var res = c.ReadStreamAsync(Direction.Forwards, stream, StreamPosition.Start, resolveLinkTos: true);
        var names = new List<string?>();
        if (await res.ReadState == ReadState.StreamNotFound) return names;
        await foreach (var re in res)
        {
            if (re.Event is null) { names.Add(null); continue; }
            names.Add(JsonNode.Parse(re.Event.Data.Span)?["Name"]?.GetValue<string>());
        }
        return names;
    }

    private static async Task WaitUntil(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return;
            await Task.Delay(500);
        }
    }
}
