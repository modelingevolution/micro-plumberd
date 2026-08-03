using FluentAssertions;
using KurrentDB.Client;
using MicroPlumberd.Testing;
using MicroPlumberd.Tests.App.Domain;
using MicroPlumberd.Tests.Utils;

namespace MicroPlumberd.Tests.Integration;

/// <summary>
/// S3 — create-new-and-swap reconciler (<see cref="UserDefinedIndexReconciler"/>) against real KurrentDB 26.1.
/// In-place index redefine is impossible (SPIKE-5/409), so a changed event-type SET yields a NEW filter-hashed
/// index name (<c>mpidx-&lt;output&gt;-&lt;hash8&gt;</c>), the read model rebuilds from the new merged view, and the
/// superseded index is DELETEd.
///
/// S3-T1 definition change → new index + rebuilt (widened) content; S3-T2 no-op when the type set is unchanged;
/// S3-T3 orphan cleanup — the old-hash index is DELETEd and no longer resolves.
/// </summary>
[TestCategory("Integration")]
public class IndexBackedMergeS3Tests
{
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly List<EventStoreServer> _servers = new();

        public async Task<(PlumberEngine Engine, IPlumber Plumber)> NewAsync(string tag)
        {
            var es = EventStoreServer.Create($"mp-s3-{tag}-{Guid.NewGuid():N}");
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

    private const string OutputStream = "S3Merge";

    private static (IReadOnlySet<string> Created, IReadOnlySet<string> CreatedAndRefined) TypeSets(PlumberEngine engine)
    {
        var all = engine.TypeHandlerRegisters.GetEventNamesFor<FooModel>().ToHashSet(StringComparer.Ordinal);
        var createdOnly = all.Where(n => n.Contains("Created")).ToHashSet(StringComparer.Ordinal);
        return (createdOnly, all);
    }

    private static async Task AppendCorpusAsync(IPlumber plumber)
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await plumber.SaveNew(FooAggregate.Open("0", a));                                   // FooCreated 0
        await plumber.SaveNew(FooAggregate.Open("1", b));                                   // FooCreated 1
        var aa = await plumber.Get<FooAggregate>(a); aa.Refine("2"); await plumber.SaveChanges(aa); // FooRefined 2
    }

    private static async Task<List<string?>> ReadIndexNamesAsync(KurrentDBClient client, string indexStream)
    {
        var names = new List<string?>();
        var read = client.ReadAllAsync(Direction.Forwards, Position.Start, StreamFilter.Prefix(indexStream),
            maxCount: long.MaxValue, resolveLinkTos: true);
        var e = read.GetAsyncEnumerator();
        try
        {
            while (true)
            {
                try { if (!await e.MoveNextAsync()) break; }
                catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.NotFound) { break; }
                if (e.Current.Event is null) continue;
                names.Add(System.Text.Json.Nodes.JsonNode.Parse(e.Current.Event.Data.Span)?["Name"]?.GetValue<string>());
            }
        }
        finally { await e.DisposeAsync(); }
        return names;
    }

    private static async Task<List<string?>> WaitIndexAsync(KurrentDBClient client, string indexStream, int atLeast)
    {
        var names = new List<string?>();
        for (var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30); DateTime.UtcNow < deadline; await Task.Delay(200))
        {
            names = await ReadIndexNamesAsync(client, indexStream);
            if (names.Count >= atLeast) return names;
        }
        return names;
    }

    // S3-T1 — a definition change (widen {Created} → {Created,Refined}) creates a NEW filter-hashed index; the
    // new index includes the Refined event; the old index is superseded.
    [Fact]
    public async Task S3T1_definition_change_creates_new_index_with_widened_content()
    {
        await using var fx = new Fixture();
        var (engine, plumber) = await fx.NewAsync("t1");
        await AppendCorpusAsync(plumber);

        var (created, all) = TypeSets(engine);
        var reconciler = new UserDefinedIndexReconciler(engine.UserDefinedIndex);

        var name1 = await reconciler.ReconcileAsync(OutputStream, created, default);
        name1.Should().Be(UserDefinedIndex.IndexNameFor(OutputStream, UserDefinedIndex.BuildFilter(created)));
        var v1 = await WaitIndexAsync(engine.Client, UserDefinedIndex.IndexStream(name1), 2);
        v1.Should().Equal(new[] { "0", "1" }, "the {Created}-only index merges only FooCreated, in commit order");

        var name2 = await reconciler.ReconcileAsync(OutputStream, all, default);
        name2.Should().NotBe(name1, "a changed event-type set deterministically yields a NEW filter-hashed name");
        name2.Should().Be(UserDefinedIndex.IndexNameFor(OutputStream, UserDefinedIndex.BuildFilter(all)));
        var v2 = await WaitIndexAsync(engine.Client, UserDefinedIndex.IndexStream(name2), 3);
        v2.Should().Equal(new[] { "0", "1", "2" }, "the widened index rebuilds to include FooRefined, in commit order");
    }

    // S3-T2 — no-op when the type set is unchanged: same name, 409 reuse, no delete of the current index.
    [Fact]
    public async Task S3T2_reconcile_is_noop_when_type_set_unchanged()
    {
        await using var fx = new Fixture();
        var (engine, plumber) = await fx.NewAsync("t2");
        await AppendCorpusAsync(plumber);

        var (_, all) = TypeSets(engine);
        var reconciler = new UserDefinedIndexReconciler(engine.UserDefinedIndex);

        var first = await reconciler.ReconcileAsync(OutputStream, all, default);
        await WaitIndexAsync(engine.Client, UserDefinedIndex.IndexStream(first), 3);

        var second = await reconciler.ReconcileAsync(OutputStream, all, default);
        second.Should().Be(first, "an unchanged type set reconciles to the SAME index name (409 reuse)");

        (await engine.UserDefinedIndex.GetFilterAsync(first)).Should()
            .NotBeNull("the current index must NOT be deleted by an unchanged reconcile");
        (await ReadIndexNamesAsync(engine.Client, UserDefinedIndex.IndexStream(first)))
            .Should().Equal(new[] { "0", "1", "2" }, "the index is untouched by an idempotent reconcile");
    }

    // S3-T3 — orphan cleanup: after a swap, the old-hash index is DELETEd (no longer resolves) and only the
    // current-hash index remains among the managed indexes for this output stream.
    [Fact]
    public async Task S3T3_superseded_index_is_deleted_after_swap()
    {
        await using var fx = new Fixture();
        var (engine, plumber) = await fx.NewAsync("t3");
        await AppendCorpusAsync(plumber);

        var (created, all) = TypeSets(engine);
        var reconciler = new UserDefinedIndexReconciler(engine.UserDefinedIndex);

        var oldName = await reconciler.ReconcileAsync(OutputStream, created, default);
        await WaitIndexAsync(engine.Client, UserDefinedIndex.IndexStream(oldName), 2);
        (await engine.UserDefinedIndex.GetFilterAsync(oldName)).Should().NotBeNull("the old index exists before the swap");

        var newName = await reconciler.ReconcileAsync(OutputStream, all, default);
        newName.Should().NotBe(oldName);

        // The reconcile deletes the superseded index. Give the DELETE a moment to take effect, then confirm it is
        // gone and the current index remains.
        var deleted = false;
        for (var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15); DateTime.UtcNow < deadline; await Task.Delay(300))
            if (await engine.UserDefinedIndex.GetFilterAsync(oldName) is null) { deleted = true; break; }

        deleted.Should().BeTrue("the superseded old-hash index must be DELETEd by the reconciler");
        (await engine.UserDefinedIndex.GetFilterAsync(newName)).Should().NotBeNull("the current index must remain");

        var prefix = UserDefinedIndex.ManagedNamePrefixFor(OutputStream);
        var managed = (await engine.UserDefinedIndex.ListNamesAsync())
            .Where(n => n.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
        managed.Should().OnlyContain(n => n == newName, "only the current-hash managed index should remain");
    }
}
