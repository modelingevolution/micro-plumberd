using FluentAssertions;
using KurrentDB.Client;
using Microsoft.Extensions.DependencyInjection;
using MicroPlumberd.Services;
using MicroPlumberd.Testing;
using MicroPlumberd.Tests.App.Domain;
using MicroPlumberd.Tests.App.Infrastructure;
using MicroPlumberd.Tests.Utils;

namespace MicroPlumberd.Tests.Integration;

/// <summary>
/// S2 — opt-in wiring + the load-bearing PROJECTION-vs-INDEX PARITY test against real KurrentDB 26.1.
///
/// S2-T1 (acceptance bar): the SAME read model fed the SAME interleaved events via the projection path and the
/// index path reaches IDENTICAL final state in the SAME delivery order. S2-T2 live-after-boot; S2-T3
/// ICaughtUpHandler fires on the index path; S2-T4 registration guards (persistent / revision-start rejected).
/// </summary>
[TestCategory("Integration")]
public class IndexBackedMergeS2Tests
{
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly List<EventStoreServer> _servers = new();

        public async Task<(PlumberEngine Engine, IPlumber Plumber)> NewAsync(string tag)
        {
            var es = EventStoreServer.Create($"mp-s2-{tag}-{Guid.NewGuid():N}");
            _servers.Add(es);
            await es.StartInDocker(inMemory: true);
            var settings = es.GetEventStoreSettings();
            return (new PlumberEngine(settings), Plumber.Create(settings));
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var s in _servers) await s.DisposeAsync();
        }
    }

    private static string? NameOf(object? payload) => payload switch
    {
        FooCreated c => c.Name,
        FooRefined r => r.Name,
        _ => null
    };

    private static string[] DeliveredOrder(CaughtUpFooModel m) =>
        m.Timeline.Where(t => t.Kind == "event").Select(t => NameOf(t.Payload)).ToArray()!;

    private static async Task<bool> WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            await Task.Delay(200);
        }
        return condition();
    }

    // Appends an interleaved FooCreated/FooRefined sequence across three aggregates; the Names are "0".."4" in
    // $all commit (call) order — the exact merged order BOTH paths must reproduce.
    private static async Task<string[]> AppendInterleavedAsync(IPlumber plumber)
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        await plumber.SaveNew(FooAggregate.Open("0", a));                                   // FooCreated 0
        await plumber.SaveNew(FooAggregate.Open("1", b));                                   // FooCreated 1
        var aa = await plumber.Get<FooAggregate>(a); aa.Refine("2"); await plumber.SaveChanges(aa); // FooRefined 2
        await plumber.SaveNew(FooAggregate.Open("3", c));                                   // FooCreated 3
        var bb = await plumber.Get<FooAggregate>(b); bb.Refine("4"); await plumber.SaveChanges(bb); // FooRefined 4
        return new[] { "0", "1", "2", "3", "4" };
    }

    // S2-T1 — THE acceptance bar. Same events, two sources (projection + index), identical final state & order.
    [Fact]
    public async Task S2T1_projection_and_index_paths_produce_identical_state_and_order()
    {
        await using var fx = new Fixture();
        var (engine, plumber) = await fx.NewAsync("t1");

        var expected = await AppendInterleavedAsync(plumber);

        var viaProjection = new CaughtUpFooModel(new InMemoryAssertionDb());
        var viaIndex = new CaughtUpFooModel(new InMemoryAssertionDb());

        // Projection path (the unchanged default) and index path (opt-in) — SAME events, SAME handler type.
        await engine.SubscribeEventHandler(viaProjection);
        await engine.SubscribeEventHandlerViaIndex(viaIndex);

        (await WaitUntil(() => viaProjection.AssertionDb.Index.Count >= 5 && viaIndex.AssertionDb.Index.Count >= 5,
            TimeSpan.FromSeconds(45))).Should().BeTrue("both paths must catch up over all 5 events");

        var projectionOrder = DeliveredOrder(viaProjection);
        var indexOrder = DeliveredOrder(viaIndex);

        projectionOrder.Should().Equal(expected, "the projection path delivers the merge in commit order");
        indexOrder.Should().Equal(expected, "the index path delivers the merge in the SAME commit order");
        indexOrder.Should().Equal(projectionOrder,
            "PARITY: index-backed merge gives exactly the ordering + no-loss + no-double-process the projection path gives");
        viaIndex.CaughtUpCount.Should().BeGreaterThanOrEqualTo(1, "the index path fires ICaughtUpHandler after history");
    }

    // S2-T2 — live after boot: with the index-backed handler running, post-boot appends are delivered live, in
    // commit order (SPIKE-1/2b end-to-end through the framework).
    [Fact]
    public async Task S2T2_index_backed_handler_tails_live_appends_after_boot()
    {
        await using var fx = new Fixture();
        var (engine, plumber) = await fx.NewAsync("t2");

        await plumber.SaveNew(FooAggregate.Open("0", Guid.NewGuid()));

        var model = new CaughtUpFooModel(new InMemoryAssertionDb());
        await engine.SubscribeEventHandlerViaIndex(model);

        (await WaitUntil(() => model.IsLive, TimeSpan.FromSeconds(45)))
            .Should().BeTrue("the index-backed handler must catch up and go live");

        await plumber.SaveNew(FooAggregate.Open("1", Guid.NewGuid()));
        await plumber.SaveNew(FooAggregate.Open("2", Guid.NewGuid()));

        (await WaitUntil(() => model.AssertionDb.Index.Count >= 3, TimeSpan.FromSeconds(30)))
            .Should().BeTrue("post-boot appends must tail into the index-backed subscription");

        DeliveredOrder(model).Should().Equal(new[] { "0", "1", "2" }, "live appends arrive in commit order");
    }

    // S2-T3 — ICaughtUpHandler is wired on the index path (regression-guards OPEN-4): CaughtUp fires (read model
    // marks itself ready/live), then live appends still tail in commit order. NOTE: unlike the projection
    // output-stream path, an index-backed subscription's history→live boundary tracks the $all position, so while
    // the index is still backfilling CaughtUp can fire before all backfilled links arrive — the guaranteed
    // properties are "CaughtUp fires" + "no loss" + "commit order", not a strict history-before-CaughtUp timeline.
    [Fact]
    public async Task S2T3_caughtup_handler_fires_and_live_tail_preserves_order_on_index_path()
    {
        await using var fx = new Fixture();
        var (engine, plumber) = await fx.NewAsync("t3");

        await plumber.SaveNew(FooAggregate.Open("0", Guid.NewGuid()));
        await plumber.SaveNew(FooAggregate.Open("1", Guid.NewGuid()));

        var model = new CaughtUpFooModel(new InMemoryAssertionDb());
        await engine.SubscribeEventHandlerViaIndex(model);

        (await WaitUntil(() => model.CaughtUpCount >= 1, TimeSpan.FromSeconds(45)))
            .Should().BeTrue("CaughtUp must fire so the read model can mark itself ready");
        model.IsLive.Should().BeTrue("CaughtUp sets IsLive");

        (await WaitUntil(() => model.AssertionDb.Index.Count >= 2, TimeSpan.FromSeconds(30)))
            .Should().BeTrue("both historical events must be delivered (no loss)");

        await plumber.SaveNew(FooAggregate.Open("2", Guid.NewGuid()));
        (await WaitUntil(() => model.AssertionDb.Index.Count >= 3, TimeSpan.FromSeconds(30)))
            .Should().BeTrue("the post-CaughtUp live append must tail in");

        DeliveredOrder(model).Should().Equal(new[] { "0", "1", "2" }, "history + live preserved in commit order");
    }

    // S2-T4 — registration guards (fail fast, no server needed): UserDefinedIndex is catch-up-only and Start/End-only.
    [Fact]
    public void S2T4_registration_guards_reject_persistent_and_revision_start()
    {
        var persistent = () => new ServiceCollection()
            .AddSingletonEventHandler<CaughtUpFooModel>(persistently: true, mergeSource: MergeSource.UserDefinedIndex);
        persistent.Should().Throw<InvalidOperationException>()
            .WithMessage("*catch-up*", "persistent + index-backed must be rejected at registration (SPIKE-7)");

        var revisionStart = () => new ServiceCollection()
            .AddSingletonEventHandler<CaughtUpFooModel>(start: FromStream.After(5), mergeSource: MergeSource.UserDefinedIndex);
        revisionStart.Should().Throw<InvalidOperationException>()
            .WithMessage("*Start or End*", "a specific revision start is meaningless on filtered $all");

        // Start and End are allowed.
        var startOk = () => new ServiceCollection()
            .AddSingletonEventHandler<CaughtUpFooModel>(start: FromStream.Start, mergeSource: MergeSource.UserDefinedIndex);
        var endOk = () => new ServiceCollection()
            .AddSingletonEventHandler<CaughtUpFooModel>(start: FromStream.End, mergeSource: MergeSource.UserDefinedIndex);
        startOk.Should().NotThrow();
        endOk.Should().NotThrow();
    }
}
