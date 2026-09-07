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
/// S4 — coexistence + the PERMANENT persistent-subscription guard + the default staying unchanged + proliferation
/// headroom, against real KurrentDB 26.1.
///
/// S4-T1 a projection-backed and an index-backed handler run side by side, both correct; S4-T2 a handler with no
/// mergeSource stays projection-backed and creates NO index; S4-T3 UserDefinedIndex+persistently is rejected
/// (permanent, SPIKE-7) while a normal persistent registration is untouched; S4-T4 many index-backed handlers in
/// one app all build (SPIKE-6 headroom regression guard).
/// </summary>
[TestCategory("Integration")]
public class IndexBackedMergeS4Tests
{
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly List<EventStoreServer> _servers = new();

        public async Task<(PlumberEngine Engine, IPlumber Plumber)> NewAsync(string tag)
        {
            var es = EventStoreServer.Create($"mp-s4-{tag}-{Guid.NewGuid():N}");
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

    private static async Task AppendCorpusAsync(IPlumber plumber)
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await plumber.SaveNew(FooAggregate.Open("0", a));
        await plumber.SaveNew(FooAggregate.Open("1", b));
        var aa = await plumber.Get<FooAggregate>(a); aa.Refine("2"); await plumber.SaveChanges(aa);
    }

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

    // S4-T1 — coexistence: a projection-backed handler and an index-backed handler build correct read models in
    // the SAME engine (the two mechanisms run side by side).
    [Fact]
    public async Task S4T1_projection_and_index_handlers_coexist()
    {
        await using var fx = new Fixture();
        var (engine, plumber) = await fx.NewAsync("t1");
        await AppendCorpusAsync(plumber);

        var projectionBacked = new FooModel(new InMemoryAssertionDb());
        var indexBacked = new CaughtUpFooModel(new InMemoryAssertionDb());

        await engine.SubscribeEventHandler(projectionBacked);
        await engine.SubscribeEventHandlerViaIndex(indexBacked);

        (await WaitUntil(() => projectionBacked.AssertionDb.Index.Count >= 3 && indexBacked.AssertionDb.Index.Count >= 3,
            TimeSpan.FromSeconds(45))).Should().BeTrue("both the projection-backed and index-backed handlers build correctly, side by side");
    }

    // S4-T2 — default unchanged: a handler subscribed with no mergeSource creates a join projection and NO managed
    // index (assert mpidx-<output>-* is absent for it).
    [Fact]
    public async Task S4T2_default_projection_handler_creates_no_index()
    {
        await using var fx = new Fixture();
        var (engine, plumber) = await fx.NewAsync("t2");
        await AppendCorpusAsync(plumber);

        var model = new FooModel(new InMemoryAssertionDb());
        await engine.SubscribeEventHandler(model); // default = projection-backed

        (await WaitUntil(() => model.AssertionDb.Index.Count >= 3, TimeSpan.FromSeconds(45)))
            .Should().BeTrue("the default projection path still builds the read model");

        var outputStream = engine.Conventions.OutputStreamModelConvention(typeof(FooModel));
        var prefix = UserDefinedIndex.ManagedNamePrefixFor(outputStream);
        var managed = (await engine.UserDefinedIndex.ListNamesAsync())
            .Where(n => n.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
        managed.Should().BeEmpty("the projection-backed default must NOT create a user-defined index");
    }

    // S4-T3 — the PERMANENT persistent guard: UserDefinedIndex+persistently is rejected at registration (SPIKE-7),
    // while a normal persistent (projection-backed) registration is untouched. Fail-fast, no server needed.
    [Fact]
    public void S4T3_persistent_index_rejected_projection_persistent_untouched()
    {
        var indexPersistent = () => new ServiceCollection()
            .AddSingletonEventHandler<CaughtUpFooModel>(persistently: true, mergeSource: MergeSource.UserDefinedIndex);
        indexPersistent.Should().Throw<InvalidOperationException>("persistent index-backing is a permanent exclusion (SPIKE-7)");

        var projectionPersistent = () => new ServiceCollection()
            .AddSingletonEventHandler<CaughtUpFooModel>(persistently: true);
        projectionPersistent.Should().NotThrow("persistent projection-backed handlers are unchanged");
    }

    // S4-T4 — proliferation headroom (SPIKE-6 regression guard): many index-backed merges in one app all build.
    [Fact]
    public async Task S4T4_many_index_backed_merges_all_build()
    {
        await using var fx = new Fixture();
        var (engine, plumber) = await fx.NewAsync("t4");
        await AppendCorpusAsync(plumber);

        var types = engine.TypeHandlerRegisters.GetEventNamesFor<FooModel>().ToHashSet(StringComparer.Ordinal);
        var reconciler = new UserDefinedIndexReconciler(engine.UserDefinedIndex);

        const int n = 6;
        var indexStreams = new List<string>();
        for (var i = 0; i < n; i++)
        {
            var name = await reconciler.ReconcileAsync($"S4Prolif{i}", types, default);
            indexStreams.Add(UserDefinedIndex.IndexStream(name));
        }

        foreach (var stream in indexStreams)
        {
            var built = false;
            for (var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30); DateTime.UtcNow < deadline; await Task.Delay(200))
            {
                var read = engine.Client.ReadAllAsync(Direction.Forwards, Position.Start, StreamFilter.Prefix(stream),
                    maxCount: long.MaxValue, resolveLinkTos: true);
                var count = 0;
                var e = read.GetAsyncEnumerator();
                try
                {
                    while (true)
                    {
                        try { if (!await e.MoveNextAsync()) break; }
                        catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.NotFound) { break; }
                        if (e.Current.Event is not null) count++;
                    }
                }
                finally { await e.DisposeAsync(); }
                if (count >= 3) { built = true; break; }
            }
            built.Should().BeTrue($"index {stream} must build the full merge — {n} concurrent index-backed merges all build");
        }
    }
}
