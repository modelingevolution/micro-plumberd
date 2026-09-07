using FluentAssertions;
using KurrentDB.Client;
using MicroPlumberd.Migration;
using MicroPlumberd.Testing;
using MicroPlumberd.Tests.App.Domain;
using MicroPlumberd.Tests.App.Infrastructure;
using Xunit;

namespace MicroPlumberd.Migration.Tests;

/// <summary>
/// The definitive proof of the SKIP design against the real incident, using a real MicroPlumberd aggregate
/// (<see cref="FooAggregate"/>) and a real <c>[OutputStream]</c> read model (<see cref="FooModel"/>, which
/// reads the merge stream <c>FooModel_v1</c>) — the OffersReadModel analog.
///
/// Flow: seed source, build the merge stream via the join projection, HARD-TOMBSTONE one aggregate stream
/// (the wedge), migrate (DropStream the tombstoned one; the copy SKIPS the merge-stream $&gt; links), then
/// boot a FRESH dest plumber and subscribe the read model. The app re-creates the linkTo projection, which
/// regenerates FooModel_v1 from the MIGRATED aggregate streams. Assert: the read model replays to a correct
/// NON-EMPTY projection (survivor present, dropped one absent), no NRE, and the regenerated merge stream has
/// NO duplicate links.
/// </summary>
[Trait("Category", "Integration")]
public class IncidentReplayIntegrationTests
{
    private sealed class Server : IAsyncDisposable
    {
        public required EventStoreServer Es { get; init; }
        public required KurrentDBClient Client { get; init; }
        public required KurrentDBProjectionManagementClient Projections { get; init; }
        public required IPlumber Plumber { get; init; }
        public string ConnectionString => Es.HttpUrl.ToString();

        public static async Task<Server> StartAsync(string tag)
        {
            var es = EventStoreServer.Create($"mp-inc-{tag}-{Guid.NewGuid():N}");
            await es.StartInDocker(inMemory: true);
            var settings = es.GetEventStoreSettings();
            return new Server
            {
                Es = es,
                Client = new KurrentDBClient(settings),
                Projections = new KurrentDBProjectionManagementClient(settings),
                Plumber = global::MicroPlumberd.Plumber.Create(settings)
            };
        }

        public async ValueTask DisposeAsync() => await Es.DisposeAsync();
    }

    private static ProjectionCopyContext ProjectionCopy(Server src, Server dst) => new()
    {
        SourceProjections = src.Projections,
        DestProjections = dst.Projections,
        SourceConnectionString = src.ConnectionString,
        PerEventPaceTimeout = TimeSpan.FromSeconds(30),
        DrainTimeout = TimeSpan.FromSeconds(60)
    };

    // The aggregate stream for an id is "<category>-<id>"; discover it empirically rather than hard-coding
    // the convention (the stream is the only non-$ stream whose name ends with the id).
    private static async Task<string> FindAggregateStreamAsync(KurrentDBClient c, Guid id)
    {
        var suffix = id.ToString();
        await foreach (var re in c.ReadAllAsync(Direction.Forwards, Position.Start))
        {
            var er = re.Event;
            if (er is null) continue;
            var s = er.EventStreamId;
            if (s.Length > 0 && s[0] != '$' && s.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return s;
        }
        throw new InvalidOperationException($"No aggregate stream found for {id}.");
    }

    private static async Task<long> CountStreamAsync(KurrentDBClient c, string stream)
    {
        var res = c.ReadStreamAsync(Direction.Forwards, stream, StreamPosition.Start, resolveLinkTos: false);
        if (await res.ReadState == ReadState.StreamNotFound) return 0;
        return await res.LongCountAsync();
    }

    // Reads the join/output stream resolving links, returning each target event's Name in order. A null
    // resolved event (the poison) is surfaced as null so the test can assert there are none.
    private static async Task<List<string?>> ReadJoinNamesAsync(KurrentDBClient c, string stream)
    {
        var res = c.ReadStreamAsync(Direction.Forwards, stream, StreamPosition.Start, resolveLinkTos: true);
        var names = new List<string?>();
        if (await res.ReadState == ReadState.StreamNotFound) return names;
        await foreach (var re in res)
        {
            if (re.Event is null) { names.Add(null); continue; } // dead link — must not happen on dest
            var node = System.Text.Json.Nodes.JsonNode.Parse(re.Event.Data.Span);
            names.Add(node?["Name"]?.GetValue<string>());
        }
        return names;
    }

    [Fact]
    public async Task Read_model_replays_rebuilt_store_to_correct_ordered_projection_without_NRE_or_duplicate_links()
    {
        await using var src = await Server.StartAsync("src");
        await using var dst = await Server.StartAsync("dst");

        // 1. Seed source in a defined order across 4 aggregate streams — the 3rd is dropped, so the
        //    survivors' relative order (live1, live2, live3) is meaningful to assert after regeneration.
        var live1 = Guid.NewGuid();
        var live2 = Guid.NewGuid();
        var deadId = Guid.NewGuid();
        var live3 = Guid.NewGuid();
        await src.Plumber.SaveNew(FooAggregate.Open("live1", live1));
        await src.Plumber.SaveNew(FooAggregate.Open("live2", live2));
        await src.Plumber.SaveNew(FooAggregate.Open("dead", deadId));
        await src.Plumber.SaveNew(FooAggregate.Open("live3", live3));

        var deadStream = await FindAggregateStreamAsync(src.Client, deadId);

        // 2. Build the [OutputStream] JOIN projection on source (fromStreams(['$et-FooCreated',…]).linkTo
        //    ('FooModel_v1')) — the ACTUAL ERP mechanism (TryCreateJoinProjection). Wait for all 4 links.
        await src.Plumber.TryCreateJoinProjection<FooModel>();
        await WaitUntil(async () => await CountStreamAsync(src.Client, "FooModel_v1") >= 4, TimeSpan.FromSeconds(30));

        // 3. Hard-tombstone the dead aggregate stream — this is the wedge: FooModel_v1 now has a link to a
        //    dead event, which NREs when resolved during replay on the SOURCE.
        await src.Client.TombstoneAsync(deadStream, StreamState.Any);

        // 4. Migrate source -> fresh dest, dropping the tombstoned stream. The copy skips the $> merge links.
        var migration = new DropStreamMigration("0001_drop_dead_offer", deadStream);
        var result = await new MigrationRunner().RunAsync(src.Client, dst.Client, [migration], dryRun: false);

        result.Copy.LinkEventsSkipped.Should().BeGreaterThan(0, "FooModel_v1 join links must be skipped, not copied");
        result.Verification!.AllOk.Should().BeTrue(result.Verification.Format());
        (await CountStreamAsync(dst.Client, deadStream)).Should().Be(0, "the tombstoned aggregate stream is dropped");

        // 5. Boot the read model on the FRESH dest. SubscribeEventHandler re-registers the JOIN projection,
        //    which REPOPULATES FooModel_v1 from the migrated (dead-free) $et-FooCreated stream; model replays.
        var model = new FooModel(new InMemoryAssertionDb());
        await dst.Plumber.SubscribeEventHandler(model);

        // Bounded stability wait — the model must reach 3 events AND hold there across consecutive reads
        // (no late dead/duplicate event), instead of a wall-clock sleep. A real dead-link NRE would leave
        // the model short; on timeout we surface the ROOT CAUSE (the resolved join links, showing any dead
        // link) so it fails LOUD as an NRE rather than as a silent empty-projection timeout.
        var stable = await WaitUntilStable(() => model.AssertionDb.Index.Count, target: 3, TimeSpan.FromSeconds(30));
        if (!stable)
        {
            var diag = await ReadJoinNamesAsync(dst.Client, "FooModel_v1");
            throw new Xunit.Sdk.XunitException(
                $"Read model did not replay to a stable 3 events (got {model.AssertionDb.Index.Count}). "
                + $"FooModel_v1 resolved to [{string.Join(", ", diag.Select(n => n ?? "<DEAD LINK / NRE>"))}] "
                + "— a <DEAD LINK / NRE> entry means a join link resolved to a deleted event (the incident).");
        }

        // 6. Read model replays to a correct NON-EMPTY projection, in order, no NRE, dropped offer absent.
        var modelNames = model.AssertionDb.Index.Values.Select(i => i.Event).OfType<FooCreated>()
            .Select(e => e.Name).ToList();
        modelNames.Should().Equal("live1", "live2", "live3");
        modelNames.Should().NotContain("dead");

        // 7. The output stream is REBUILT correctly by the typical projection: same survivors, in order,
        //    every link resolving (no dead link), and NO duplicate links (proves skip-not-rebuild was right —
        //    a manual rebuild would have doubled these once the projection re-ran).
        var joinNames = await ReadJoinNamesAsync(dst.Client, "FooModel_v1");
        joinNames.Should().NotContainNulls("no join link may resolve to a dead event");
        joinNames.Should().Equal("live1", "live2", "live3");
        (await CountStreamAsync(dst.Client, "FooModel_v1")).Should().Be(3, "exactly one link per survivor — no duplicates");
    }

    // Seeds one aggregate 'a' and one 'b', each FooCreated then FooRefined, in INTERLEAVED $all arrival order
    // [a-created, a-refined, b-created, b-refined], then creates the [OutputStream] JOIN projection on source.
    private static async Task SeedInterleavedAsync(Server src, Guid a, Guid b)
    {
        await src.Plumber.SaveNew(FooAggregate.Open("a-created", a));            // FooCreated(a)
        var aggA = await src.Plumber.Get<FooAggregate>(a);
        aggA.Refine("a-refined"); await src.Plumber.SaveChanges(aggA);           // FooRefined(a)
        await src.Plumber.SaveNew(FooAggregate.Open("b-created", b));            // FooCreated(b)
        var aggB = await src.Plumber.Get<FooAggregate>(b);
        aggB.Refine("b-refined"); await src.Plumber.SaveChanges(aggB);           // FooRefined(b)
        await src.Plumber.TryCreateJoinProjection<FooModel>();
        await WaitUntil(async () => await CountStreamAsync(src.Client, "FooModel_v1") >= 4, TimeSpan.FromSeconds(30));
    }

    // THE DETERMINISM GATE. The paced PROJECTION COPY (pre-create the fromStreams join projection on the empty
    // dest, then feed events one at a time waiting for each link before the next) must rebuild the merge stream
    // in ARRIVAL order EVERY time. The earlier end-only-drain version was 7/8 (a backlog type-clustered once);
    // this runs the interleaved recovery 15× and requires 15/15 — a single cluster fails it loudly.
    [Fact]
    public async Task Paced_projection_copy_preserves_interleaved_order_deterministically_15x()
    {
        await using var src = await Server.StartAsync("detsrc");
        await SeedInterleavedAsync(src, Guid.NewGuid(), Guid.NewGuid());
        var expected = new[] { "a-created", "a-refined", "b-created", "b-refined" };

        var observed = new List<string>();
        for (var i = 0; i < 15; i++)
        {
            await using var dst = await Server.StartAsync($"detdst{i}");
            await new MigrationRunner().RunAsync(src.Client, dst.Client, Array.Empty<Migration>(), dryRun: false,
                ProjectionCopy(src, dst));
            await WaitUntil(async () => await CountStreamAsync(dst.Client, "FooModel_v1") >= 4, TimeSpan.FromSeconds(30));
            var order = await ReadJoinNamesAsync(dst.Client, "FooModel_v1");
            observed.Add(string.Join(",", order.Select(n => n ?? "<DEAD>")));
        }

        observed.Should().OnlyContain(o => o == string.Join(",", expected),
            "paced projection copy must be DETERMINISTIC — 15/15 in arrival order. Observed per run: [{0}]",
            string.Join(" | ", observed));
    }

    // THE END-TO-END RECOVERY PROOF (paced projection copy). The real :5081 fix: a tombstoned aggregate stream
    // poisons the merge stream (dead link → NRE). The migration drops it, PRE-CREATES the join projection on the
    // empty dest, and PACES the copy so the projection rebuilds FooModel_v1 in commit order as the sole writer;
    // the app boots and NO-OPs (mp_query_hash match). Interleaved multi-type fixture makes clustering fail loudly.
    [Fact]
    public async Task Paced_projection_copy_recovers_tombstoned_store_in_commit_order_without_NRE()
    {
        await using var src = await Server.StartAsync("recsrc");
        await using var dst = await Server.StartAsync("recdst");

        var a = Guid.NewGuid();
        var dead = Guid.NewGuid();
        var b = Guid.NewGuid();
        await src.Plumber.SaveNew(FooAggregate.Open("a-created", a));
        var aggA = await src.Plumber.Get<FooAggregate>(a);
        aggA.Refine("a-refined"); await src.Plumber.SaveChanges(aggA);
        await src.Plumber.SaveNew(FooAggregate.Open("dead", dead));      // poisons the merge stream, then dropped
        await src.Plumber.SaveNew(FooAggregate.Open("b-created", b));
        var aggB = await src.Plumber.Get<FooAggregate>(b);
        aggB.Refine("b-refined"); await src.Plumber.SaveChanges(aggB);

        var deadStream = await FindAggregateStreamAsync(src.Client, dead);
        await src.Plumber.TryCreateJoinProjection<FooModel>();
        await WaitUntil(async () => await CountStreamAsync(src.Client, "FooModel_v1") >= 5, TimeSpan.FromSeconds(30));
        await src.Client.TombstoneAsync(deadStream, StreamState.Any); // the wedge (dead link in FooModel_v1)

        // Migrate: drop the tombstoned stream + PACED projection copy → the pre-created join projection rebuilds
        // FooModel_v1 in commit order from the migrated (dead-free) store.
        var migration = new DropStreamMigration("0001_drop_dead_offer", deadStream);
        var result = await new MigrationRunner().RunAsync(src.Client, dst.Client, [migration], dryRun: false,
            ProjectionCopy(src, dst));

        result.CopiedProjections.Should().Contain("FooModel_v1", "the app's join projection is pre-created on the dest");
        result.Copy.LinkEventsSkipped.Should().BeGreaterThan(0, "source FooModel_v1 $> links are skipped, not copied");
        result.Verification!.AllOk.Should().BeTrue(result.Verification.Format());
        (await CountStreamAsync(dst.Client, deadStream)).Should().Be(0, "the tombstoned stream is dropped");

        // Merge stream built by the paced projection: survivors only, commit order, no dead link, no duplicates.
        await WaitUntil(async () => await CountStreamAsync(dst.Client, "FooModel_v1") >= 4, TimeSpan.FromSeconds(30));
        var joinNames = await ReadJoinNamesAsync(dst.Client, "FooModel_v1");
        joinNames.Should().NotContainNulls("no join link may resolve to a dead event");
        joinNames.Should().Equal(new[] { "a-created", "a-refined", "b-created", "b-refined" },
            "paced projection copy must preserve commit order, not type-cluster");
        joinNames.Should().NotContain("dead");
        (await CountStreamAsync(dst.Client, "FooModel_v1")).Should().Be(4, "one link per survivor — no duplicates");

        // The read model replays clean over the pre-built dest — NON-EMPTY, in order, NO NRE.
        var model = new FooModel(new InMemoryAssertionDb());
        await dst.Plumber.SubscribeEventHandler(model);
        var stable = await WaitUntilStable(() => model.AssertionDb.Index.Count, target: 4, TimeSpan.FromSeconds(30));
        if (!stable)
        {
            var diag = await ReadJoinNamesAsync(dst.Client, "FooModel_v1");
            throw new Xunit.Sdk.XunitException(
                $"Read model did not replay to a stable 4 events (got {model.AssertionDb.Index.Count}). "
                + $"FooModel_v1 resolved to [{string.Join(", ", diag.Select(n => n ?? "<DEAD LINK / NRE>"))}].");
        }

        // Simulated APP BOOT: fresh plumber re-registers the join projection → NO-OP (mp_query_hash match), so
        // the merge stream is left untouched (no restart / re-link / reorder / duplicate).
        var countBefore = await CountStreamAsync(dst.Client, "FooModel_v1");
        var bootPlumber = global::MicroPlumberd.Plumber.Create(dst.Es.GetEventStoreSettings());
        (await bootPlumber.TryCreateJoinProjection<FooModel>()).Should().BeFalse("mp_query_hash matches → app no-ops");
        await Task.Delay(1000);
        (await ReadJoinNamesAsync(dst.Client, "FooModel_v1")).Should().Equal(new[] { "a-created", "a-refined", "b-created", "b-refined" });
        (await CountStreamAsync(dst.Client, "FooModel_v1")).Should().Be(countBefore, "link count stable across the no-op boot");
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

    // Polls value until it reaches target AND holds that value across two consecutive reads (stable), or
    // times out. Returns whether stability at the target was observed.
    private static async Task<bool> WaitUntilStable(Func<int> value, int target, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var prev = -1;
        while (DateTime.UtcNow < deadline)
        {
            var cur = value();
            if (cur >= target && cur == prev) return true;
            prev = cur;
            await Task.Delay(500);
        }
        return false;
    }

    /// <summary>A raw DropStream migration over an exact stream name (discovered at runtime).</summary>
    private sealed class DropStreamMigration(string id, string stream) : Migration
    {
        public override string Id => id;
        public override void Migrate(IMigrationBuilder b) => b.DropStream(stream);
    }
}
