using FluentAssertions;
using KurrentDB.Client;
using MicroPlumberd.Testing;
using MicroPlumberd.Tests.App.Domain;
using MicroPlumberd.Tests.App.Infrastructure;
using MicroPlumberd.Tests.Utils;

namespace MicroPlumberd.Tests.Integration;

/// <summary>
/// S1 — the index-backed subscription PRIMITIVE + the refactored <see cref="SubscriptionRunner"/> seam, against a
/// REAL KurrentDB 26.1 (<see cref="EventStoreServer"/> Docker, NEVER Testcontainers). Exercises the SHIPPED types
/// (<see cref="IndexSubscriptionState"/>, <see cref="UserDefinedIndex"/>, the <c>SubscriptionRunnerState</c> guard)
/// through real MicroPlumberd events (FooCreated/FooRefined) — not ad-hoc client calls.
///
/// S1-T1 catch-up→live in commit order; S1-T2 resume from a recorded $all position; S1-T3 the SubscribeToStream
/// footgun guard; S1-T4 the stream-backed path still delivers (the refactor changed the seam, not the behaviour).
/// (S1-T5 Migration parity is the pre-existing UserDefinedIndexIntegrationTests, unchanged by the relocation.)
/// </summary>
[TestCategory("Integration")]
public class IndexBackedMergeS1Tests
{
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly List<EventStoreServer> _servers = new();

        public async Task<(KurrentDBClientSettings Settings, PlumberEngine Engine, IPlumber Plumber)> NewAsync(string tag)
        {
            var es = EventStoreServer.Create($"mp-s1-{tag}-{Guid.NewGuid():N}");
            _servers.Add(es);
            await es.StartInDocker(inMemory: true);
            var settings = es.GetEventStoreSettings();
            return (settings, new PlumberEngine(settings), Plumber.Create(settings));
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

    private static async Task AppendFoo(IPlumber plumber, string name) =>
        await plumber.SaveNew(FooAggregate.Open(name, Guid.NewGuid()));

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

    // Count materialised index links via the filtered-$all read, tolerating the "still building" NotFound
    // (the index read stream 404s until the backfill has written links). Test scaffolding only.
    private static async Task<int> IndexLinkCount(KurrentDBClient client, string indexStream)
    {
        var read = client.ReadAllAsync(Direction.Forwards, Position.Start, StreamFilter.Prefix(indexStream),
            maxCount: long.MaxValue, resolveLinkTos: true);
        var e = read.GetAsyncEnumerator();
        var n = 0;
        try
        {
            while (true)
            {
                try { if (!await e.MoveNextAsync()) break; }
                catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.NotFound) { return 0; }
                if (e.Current.Event is not null) n++;
            }
        }
        finally { await e.DisposeAsync(); }
        return n;
    }

    private async Task<(string Name, string IndexStream)> EnsureIndexForFooModelAsync(PlumberEngine engine)
    {
        var outputStream = engine.Conventions.OutputStreamModelConvention(typeof(CaughtUpFooModel));
        var types = engine.TypeHandlerRegisters.GetEventNamesFor<CaughtUpFooModel>().ToHashSet(StringComparer.Ordinal);
        var filter = UserDefinedIndex.BuildFilter(types);
        var name = UserDefinedIndex.IndexNameFor(outputStream, filter);
        await engine.UserDefinedIndex.EnsureAsync(name, types);
        return (name, UserDefinedIndex.IndexStream(name));
    }

    // S1-T1 — index-backed subscription catches up over history THEN tails live appends, in commit order,
    // driven by IndexSubscriptionState inside a SubscriptionRunner (the shared loop), through production types.
    [Fact]
    public async Task S1T1_index_backed_subscription_catches_up_then_tails_in_commit_order()
    {
        await using var fx = new Fixture();
        var (_, engine, plumber) = await fx.NewAsync("t1");

        // History: three FooCreated events named "0","1","2" (three streams → interleaved in $all commit order).
        for (var i = 0; i < 3; i++) await AppendFoo(plumber, i.ToString());

        var (_, indexStream) = await EnsureIndexForFooModelAsync(engine);

        var model = new CaughtUpFooModel(new InMemoryAssertionDb());
        using var cts = new CancellationTokenSource();
        var state = new IndexSubscriptionState(engine.Client, indexStream, FromAll.Start, null, cts.Token);
        var runner = new SubscriptionRunner(engine, state);
        await runner.WithHandler(model, engine.TypeHandlerRegisters.GetEventNameConverterFor<CaughtUpFooModel>()!);

        (await WaitUntil(() => model.AssertionDb.Index.Count >= 3, TimeSpan.FromSeconds(30)))
            .Should().BeTrue("the index-backed subscription must catch up over the 3 historical events");

        // Live: append two more AFTER the subscription is open; the SAME subscription must tail them.
        await AppendFoo(plumber, "3");
        await AppendFoo(plumber, "4");

        (await WaitUntil(() => model.AssertionDb.Index.Count >= 5, TimeSpan.FromSeconds(30)))
            .Should().BeTrue("post-subscription appends must be pushed to the open filtered-$all subscription");

        await cts.CancelAsync();
        await runner.DisposeAsync();

        var delivered = model.Timeline.Where(t => t.Kind == "event").Select(t => NameOf(t.Payload)).ToArray();
        delivered.Should().Equal(new[] { "0", "1", "2", "3", "4" },
            "history + live must arrive in $all commit order via the index");
    }

    // S1-T2 — resume: record the last $all position after 0,1,2, dispose, append 3,4, resubscribe from the
    // recorded FromAll position → ONLY 3,4 are delivered (no replay of 0,1,2). Mirrors SPIKE-3.
    [Fact]
    public async Task S1T2_resume_from_recorded_position_delivers_only_new_events()
    {
        await using var fx = new Fixture();
        var (_, engine, plumber) = await fx.NewAsync("t2");

        for (var i = 0; i < 3; i++) await AppendFoo(plumber, i.ToString());
        var (_, indexStream) = await EnsureIndexForFooModelAsync(engine);
        var filter = new SubscriptionFilterOptions(StreamFilter.Prefix(indexStream));

        // Deterministic scaffolding: wait for the index to materialise its 3 history links before pass 1 opens a
        // raw (non-retrying) subscription. The SHIPPED path (IndexSubscriptionState in SubscriptionRunner) instead
        // tolerates the transient "still building" NotFound via resubscribe — exercised in S1T1.
        var indexReady = false;
        for (var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30); DateTime.UtcNow < deadline; await Task.Delay(200))
            if (await IndexLinkCount(engine.Client, indexStream) >= 3) { indexReady = true; break; }
        indexReady.Should().BeTrue("the index must backfill its 3 history links before the checkpoint pass");

        // Pass 1: consume 0,1,2 and record the last delivered link's $all position.
        var firstPass = new List<string?>();
        Position? checkpoint = null;
        using (var cts1 = new CancellationTokenSource())
        {
            var pump = Task.Run(async () =>
            {
                await using var sub = engine.Client.SubscribeToAll(FromAll.Start, resolveLinkTos: true,
                    filterOptions: filter, cancellationToken: cts1.Token);
                try
                {
                    await foreach (var m in sub.Messages.WithCancellation(cts1.Token))
                        if (m is StreamMessage.Event(var e))
                        {
                            firstPass.Add(System.Text.Json.Nodes.JsonNode.Parse(e.Event.Data.Span)?["Name"]?.GetValue<string>());
                            if (e.OriginalPosition is { } p) checkpoint = p;
                        }
                }
                catch (OperationCanceledException) { }
            });
            await WaitUntil(() => firstPass.Count >= 3, TimeSpan.FromSeconds(30));
            await cts1.CancelAsync();
            await pump;
        }

        firstPass.Should().Equal(new[] { "0", "1", "2" });
        checkpoint.Should().NotBeNull("a resumable $all position must be recoverable from the index link event");

        await AppendFoo(plumber, "3");
        await AppendFoo(plumber, "4");

        // Pass 2: resume via IndexSubscriptionState constructed at FromAll.After(checkpoint) — the shipped type.
        var model = new CaughtUpFooModel(new InMemoryAssertionDb());
        using var cts2 = new CancellationTokenSource();
        var state = new IndexSubscriptionState(engine.Client, indexStream, FromAll.After(checkpoint!.Value), null, cts2.Token);
        var runner = new SubscriptionRunner(engine, state);
        await runner.WithHandler(model, engine.TypeHandlerRegisters.GetEventNameConverterFor<CaughtUpFooModel>()!);

        (await WaitUntil(() => model.AssertionDb.Index.Count >= 2, TimeSpan.FromSeconds(30)))
            .Should().BeTrue("resume must deliver the two events after the checkpoint");
        await Task.Delay(1000); // give any erroneous replay of 0,1,2 a chance to show up

        await cts2.CancelAsync();
        await runner.DisposeAsync();

        var delivered = model.Timeline.Where(t => t.Kind == "event").Select(t => NameOf(t.Payload)).ToArray();
        delivered.Should().Equal(new[] { "3", "4" },
            "resuming from the recorded position yields ONLY events after it — no replay of 0,1,2");
    }

    // S1-T3 — the loud guard: SubscribeToStream against a $idx-user-… name is the SPIKE-2a/SPIKE-9 silent-zero
    // footgun; StreamSubscriptionState.Subscribe() must throw InvalidOperationException instead of hanging silent.
    [Fact]
    public void S1T3_subscribe_to_index_stream_via_stream_state_throws()
    {
        using var client = new KurrentDBClient(KurrentDBClientSettings.Create("esdb://localhost:2113?tls=false"));
        var state = new SubscriptionRunnerState(FromStream.Start, client,
            UserDefinedIndex.IndexStream("some-merge"), null, CancellationToken.None);

        var act = () => state.Subscribe();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*index*")
            .Which.Message.Should().Contain("SubscribeToAll");
    }

    // S1-T4 — no regression: the stream-backed (projection output-stream) path still delivers in order after the
    // ISubscriptionState refactor (control — the seam changed, not the behaviour).
    [Fact]
    public async Task S1T4_stream_backed_subscription_still_delivers_in_order()
    {
        await using var fx = new Fixture();
        var (_, _, plumber) = await fx.NewAsync("t4");

        await plumber.TryCreateJoinProjection<CaughtUpFooModel>();
        for (var i = 0; i < 3; i++) await AppendFoo(plumber, i.ToString());
        await Task.Delay(2000); // let the projection link the 3 events

        var model = new CaughtUpFooModel(new InMemoryAssertionDb());
        await plumber.SubscribeEventHandler(model, ensureOutputStreamProjection: false);

        (await WaitUntil(() => model.AssertionDb.Index.Count >= 3, TimeSpan.FromSeconds(30)))
            .Should().BeTrue("the stream-backed path must still catch up over history");

        var delivered = model.Timeline.Where(t => t.Kind == "event").Select(t => NameOf(t.Payload)).ToArray();
        delivered.Should().Equal(new[] { "0", "1", "2" }, "stream-backed delivery order is unchanged by the refactor");
    }
}
