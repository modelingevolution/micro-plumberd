using KurrentDB.Client;
using FluentAssertions;
using MicroPlumberd.Services;
using MicroPlumberd.Testing;
using MicroPlumberd.Tests.App.Domain;
using MicroPlumberd.Tests.App.Infrastructure;
using MicroPlumberd.Tests.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace MicroPlumberd.Tests.Integration;

[TestCategory("Integration")]
public class ReadModelTests : IAsyncDisposable, IDisposable
{
    private readonly EventStoreServer _eventStore;

    private IPlumber plumber;

    public ReadModelTests()
    {
        _eventStore = new EventStoreServer();
        plumber = Plumber.Create(_eventStore.GetEventStoreSettings());
    }

    /// <summary>
    /// A persistent subscription from the start delivers the event that was there, then what follows — checked by identity
    /// with a FENCE (as SubscribeModelFromEnd): one subscription delivers in order, so once the event appended after
    /// subscribing has arrived, the delivered sequence must be exactly [the pre-subscribe event, the fence]. It waited a
    /// fixed second and counted, which passed only when the server's projection and the subscription were both fast enough.
    /// </summary>
    [Fact]
    public async Task SubscribeModelPersistently()
    {
        await _eventStore.StartInDocker();
        var before = await AppendOneEvent();

        // As SubscribeModel: the projection holds the pre-subscribe event BEFORE the group is created, or Start and End
        // would be the same (empty) position and "from the start" could not be told from "from the end".
        await plumber.TryCreateJoinProjection<FooModel>();
        var modelStream = plumber.Config.Conventions.OutputStreamModelConvention(typeof(FooModel));
        var linked = await Eventually(() => CountIn(modelStream), n => n == 1);
        linked.Should().Be(1, $"the projection must link the pre-subscribe event into {modelStream} first — it held {linked} of 1 after 30 s");

        var fooModel = new FooModel(new InMemoryAssertionDb());
        await using var sub = await plumber.SubscribeEventHandlerPersistently(fooModel, startFrom: StreamPosition.Start,
            ensureOutputStreamProjection: false);

        var fence = await AppendOneEvent();
        var delivered = await Eventually(() => Task.FromResult(fooModel.AssertionDb.Index.Count), n => n >= 2);
        delivered.Should().BeGreaterThanOrEqualTo(2, $"the event before subscribing and the fence are delivered — held {delivered} of 2 after 30 s");

        // In order, nothing else: the pre-subscribe event, then the fence.
        fooModel.AssertionDb.Index.Values.Select(i => i.Metadata.Id).Should().Equal(before, fence);
    }
    /// <summary>
    /// "From End-1" delivers the LAST event of the model's stream, then what follows. ORDER-INDEPENDENT (epic-099 card
    /// microplumberd-readmodeltests-order-dependency: red 2/2 inside its class, green alone): End-1 is resolved over the
    /// model's join-projection stream, which the SERVER fills asynchronously — 100 ms after the appends it could hold fewer
    /// than both, and End-1 then pointed elsewhere; fixed delays then asserted on whichever race was won. Now the projection
    /// stream must hold both events BEFORE the subscription resolves End-1, and the claim is checked by IDENTITY with a
    /// FENCE (rev-reach 346ee9e75): one subscription delivers in order, so once the event appended AFTER subscribing has
    /// arrived, everything before it has too — the delivered sequence must be exactly [the last pre-subscribe event, the
    /// new one]. A wrong End-1 is red under any load; no settle window is needed.
    /// </summary>
    [Fact]
    public async Task SubscribeModelFromEnd()
    {
        await _eventStore.StartInDocker();
        await AppendOneEvent();
        var last = await AppendOneEvent();

        await plumber.TryCreateJoinProjection<FooModel>();
        var modelStream = plumber.Config.Conventions.OutputStreamModelConvention(typeof(FooModel));
        var linked = await Eventually(() => CountIn(modelStream), n => n == 2);
        linked.Should().Be(2, $"the projection must link both events into {modelStream} before End-1 is resolved over it — " +
                              $"it held {linked} of 2 after 30 s");

        var fooModel = new FooModel(new InMemoryAssertionDb());
        // The projection exists and is verified populated (above), so the subscription must not ensure it again: an
        // immediate second ensure of a just-created projection is answered Conflict by the server (measured).
        await using var sub = await plumber.SubscribeEventHandler(fooModel, start: FromRelativeStreamPosition.End - 1,
            ensureOutputStreamProjection: false);

        var appended = await AppendOneEvent();   // the FENCE
        var delivered = await Eventually(() => Task.FromResult(fooModel.AssertionDb.Index.Count), n => n >= 2);
        delivered.Should().BeGreaterThanOrEqualTo(2, $"the fence and the event before it are delivered — held {delivered} of 2 after 30 s");

        // From End-1, in order: the last pre-subscribe event, then the fence — nothing earlier, nothing between.
        fooModel.AssertionDb.Index.Values.Select(i => i.Metadata.Id).Should().Equal(last, appended);
    }

    /// <summary>
    /// Observes until <paramref name="done"/> holds or 30 s pass, and returns the LAST observed value either way — so a
    /// timed-out assertion says what it saw ("held 1 of 2"), never only that it waited.
    /// </summary>
    private static async Task<T> Eventually<T>(Func<Task<T>> observe, Func<T, bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        var seen = await observe();
        while (!done(seen) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
            seen = await observe();
        }
        return seen;
    }

    /// <summary>How many events a stream holds (links counted as events), 0 when it does not exist yet.</summary>
    private async Task<int> CountIn(string stream)
    {
        var read = plumber.Client.ReadStreamAsync(Direction.Forwards, stream, StreamPosition.Start, resolveLinkTos: false);
        if (await read.ReadState == ReadState.StreamNotFound) return 0;
        return await read.CountAsync();
    }
    [Fact]
    /// <summary>
    /// A subscription from the start: [the pre-subscribe event, the fence], in order — fenced by identity, no fixed wait.
    /// The projection must hold the pre-subscribe event BEFORE subscribing: created at subscribe time it is still empty, its
    /// End IS its start, and "from the start" could not be told from "from the end" (that mutant survived until this wait).
    /// </summary>
    public async Task SubscribeModel()
    {
        await _eventStore.StartInDocker();
        var before = await AppendOneEvent();

        await plumber.TryCreateJoinProjection<FooModel>();
        var modelStream = plumber.Config.Conventions.OutputStreamModelConvention(typeof(FooModel));
        var linked = await Eventually(() => CountIn(modelStream), n => n == 1);
        linked.Should().Be(1, $"the projection must link the pre-subscribe event into {modelStream} first — it held {linked} of 1 after 30 s");

        var fooModel = new FooModel(new InMemoryAssertionDb());
        await using var sub = await plumber.SubscribeEventHandler(fooModel, ensureOutputStreamProjection: false);

        var fence = await AppendOneEvent();
        var delivered = await Eventually(() => Task.FromResult(fooModel.AssertionDb.Index.Count), n => n >= 2);
        delivered.Should().BeGreaterThanOrEqualTo(2, $"the event before subscribing and the fence are delivered — held {delivered} of 2 after 30 s");

        fooModel.AssertionDb.Index.Values.Select(i => i.Metadata.Id).Should().Equal(before, fence);
    }
    [Fact]
    public async Task SubscribeModelWithEventStoreRestart()
    {
        await _eventStore.StartInDocker(inMemory:false);
        await AppendOneEvent();

        var fooModel = new FooModel(new InMemoryAssertionDb());

        var sub = await plumber.SubscribeEventHandler(fooModel);

        await Task.Delay(1000);

        fooModel.AssertionDb.Index.Should().HaveCount(1);

        await _eventStore.Restart(TimeSpan.FromSeconds(5));
        await AppendOneEvent();
        await Task.Delay(1000);
        fooModel.AssertionDb.Index.Should().HaveCount(2);

    }
    [Fact]
    public async Task TryCreateJoinProjection_SkipsRecreate_OnSecondBoot_WhenQueryUnchanged()
    {
        await _eventStore.StartInDocker();

        var firstResult = await plumber.TryCreateJoinProjection<FooModel>();
        firstResult.Should().BeTrue("the projection does not exist on first boot");

        var plumber2 = Plumber.Create(_eventStore.GetEventStoreSettings());

        var secondResult = await plumber2.TryCreateJoinProjection<FooModel>();
        secondResult.Should().BeFalse("the projection's query is unchanged since first boot");
    }

    /// <summary>
    /// The lookup query is only ever compiled server-side, so a malformed guard would ship undetected:
    /// invalid syntax throws on create, a query the engine rejects at runtime leaves the projection Faulted.
    /// </summary>
    [Fact]
    public async Task EnsureLookupProjection_EmitsQuery_ProjectionEngineAccepts()
    {
        await _eventStore.StartInDocker();

        var created = await plumber.ProjectionManagementClient.EnsureLookupProjection(plumber.Client,
            plumber.ProjectionRegister, nameof(FooAggregate), "RecipientId", LookupProjectionName);

        created.Should().BeTrue("the lookup projection does not exist on first boot");
        (await WaitForSettledStatus(LookupProjectionName)).Should().Be("Running");
    }

    private const string LookupProjectionName = "FooLookup";

    private async Task<string?> WaitForSettledStatus(string projection)
    {
        for (int i = 0; i < 50; i++)
        {
            var status = (await plumber.ProjectionManagementClient.GetStatusAsync(projection))?.Status;
            if (status is "Running" or "Faulted") return status;
            await Task.Delay(200);
        }

        return (await plumber.ProjectionManagementClient.GetStatusAsync(projection))?.Status;
    }

    [Fact]
    public async Task SubscribeScopedModel()
    {
        // TODO: Switch to EF to check
        await _eventStore.StartInDocker();
        await AppendOneEvent();

        var sp = new ServiceCollection()
            .AddPlumberd(_eventStore.GetEventStoreSettings())
            .AddEventHandler<FooModel>()
            .BuildServiceProvider();
        using var scope = sp.CreateScope();

        plumber = scope.ServiceProvider.GetRequiredService<IPlumber>();

        var sub = await plumber.SubscribeEventHandler<FooModel>();

        await Task.Delay(1000);
        // Should check db.
    }

    /// <summary>Appends one FooCreated (a new aggregate) and returns its id — the identity a delivery carries (Metadata.Id).</summary>
    private async Task<Guid> AppendOneEvent()
    {
        FooAggregate aggregate = FooAggregate.Open("Hello");
        await plumber.SaveNew(aggregate);
        return aggregate.Id;
    }

    public async ValueTask DisposeAsync()
    {
        await _eventStore.DisposeAsync();
    }

    public void Dispose()
    {
        _eventStore.Dispose();
    }
}