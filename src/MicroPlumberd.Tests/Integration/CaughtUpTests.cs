using EventStore.Client;
using FluentAssertions;
using MicroPlumberd.Testing;
using MicroPlumberd.Tests.App.Domain;
using MicroPlumberd.Tests.App.Infrastructure;
using MicroPlumberd.Tests.Utils;

namespace MicroPlumberd.Tests.Integration;

/// <summary>
/// Tests for <see cref="ICaughtUpHandler"/> behavior.
///
/// Uses <c>TryCreateJoinProjection</c> + delay before subscribing so that
/// the output stream already contains historical events when the subscription starts.
/// Without this, the projection-creation race causes CaughtUp to fire immediately
/// on an empty output stream.
/// </summary>
[TestCategory("Integration")]
public class CaughtUpTests : IAsyncDisposable, IDisposable
{
    private readonly EventStoreServer _eventStore;
    private IPlumber plumber;

    public CaughtUpTests()
    {
        _eventStore = new EventStoreServer();
        plumber = Plumber.Create(_eventStore.GetEventStoreSettings());
    }

    [Fact]
    public async Task CaughtUp_called_after_historical_events()
    {
        await _eventStore.StartInDocker();

        // Pre-create the projection so the output stream gets populated
        await plumber.TryCreateJoinProjection<CaughtUpFooModel>();

        // Append 3 events and give the projection time to link them
        await AppendOneEvent();
        await AppendOneEvent();
        await AppendOneEvent();
        await Task.Delay(2000);

        var model = new CaughtUpFooModel(new InMemoryAssertionDb());
        await plumber.SubscribeEventHandler(model, ensureOutputStreamProjection: false);

        await Task.Delay(2000);

        model.AssertionDb.Index.Should().HaveCount(3);
        model.IsLive.Should().BeTrue();
        model.CaughtUpCount.Should().Be(1);

        // Historical events should appear BEFORE CaughtUp in the timeline
        var caughtUpIndex = model.Timeline.FindIndex(t => t.Kind == "caught-up");
        caughtUpIndex.Should().Be(3, "caught-up should appear after all 3 historical events");
    }

    [Fact]
    public async Task Events_processed_while_IsLive_is_false()
    {
        await _eventStore.StartInDocker();

        await plumber.TryCreateJoinProjection<CaughtUpFooModel>();

        await AppendOneEvent();
        await AppendOneEvent();
        await Task.Delay(2000);

        var model = new CaughtUpFooModel(new InMemoryAssertionDb());
        await plumber.SubscribeEventHandler(model, ensureOutputStreamProjection: false);

        await Task.Delay(2000);

        model.AssertionDb.Index.Should().HaveCount(2);
        model.IsLive.Should().BeTrue("IsLive should be true after CaughtUp");

        // The 2 historical events should have IsLiveAtTime == false
        var historicalEvents = model.Timeline.Where(t => t.Kind == "event").ToList();
        historicalEvents.Should().HaveCount(2);
        historicalEvents.Should().AllSatisfy(e =>
            e.IsLiveAtTime.Should().BeFalse("historical events should be processed while IsLive is false"));

        // The CaughtUp entry should be the first with IsLiveAtTime == true
        var caughtUpEntry = model.Timeline.First(t => t.Kind == "caught-up");
        caughtUpEntry.IsLiveAtTime.Should().BeTrue("CaughtUp sets IsLive to true");
    }

    [Fact]
    public async Task CaughtUp_fires_between_historical_and_live_events()
    {
        await _eventStore.StartInDocker();

        await plumber.TryCreateJoinProjection<CaughtUpFooModel>();

        // Append 2 events before subscribing
        await AppendOneEvent();
        await AppendOneEvent();
        await Task.Delay(2000);

        var model = new CaughtUpFooModel(new InMemoryAssertionDb());
        await plumber.SubscribeEventHandler(model, ensureOutputStreamProjection: false);

        await Task.Delay(2000);

        model.IsLive.Should().BeTrue();
        model.AssertionDb.Index.Should().HaveCount(2);

        // Append 2 more live events
        await AppendOneEvent();
        await AppendOneEvent();

        await Task.Delay(1000);

        model.AssertionDb.Index.Should().HaveCount(4);
        model.CaughtUpCount.Should().Be(1);

        // Timeline: event, event, caught-up, event, event
        var caughtUpIndex = model.Timeline.FindIndex(t => t.Kind == "caught-up");
        caughtUpIndex.Should().Be(2, "caught-up should appear after 2 historical events");

        var eventsAfterCaughtUp = model.Timeline.Skip(caughtUpIndex + 1)
            .Where(t => t.Kind == "event").ToList();
        eventsAfterCaughtUp.Should().HaveCount(2, "2 live events should follow caught-up");
    }

    [Fact]
    public async Task CaughtUp_called_immediately_on_empty_stream()
    {
        await _eventStore.StartInDocker();

        var model = new CaughtUpFooModel(new InMemoryAssertionDb());
        await plumber.SubscribeEventHandler(model);

        await Task.Delay(2000);

        model.IsLive.Should().BeTrue();
        model.CaughtUpCount.Should().Be(1);
        model.AssertionDb.Index.Should().BeEmpty();

        model.Timeline.Should().HaveCount(1);
        model.Timeline[0].Kind.Should().Be("caught-up");
    }

    [Fact]
    public async Task CaughtUp_then_live_events_on_empty_stream()
    {
        await _eventStore.StartInDocker();

        var model = new CaughtUpFooModel(new InMemoryAssertionDb());
        await plumber.SubscribeEventHandler(model);

        await Task.Delay(2000);

        model.IsLive.Should().BeTrue();

        await AppendOneEvent();
        await AppendOneEvent();

        await Task.Delay(1000);

        model.AssertionDb.Index.Should().HaveCount(2);
        model.CaughtUpCount.Should().Be(1);

        model.Timeline[0].Kind.Should().Be("caught-up");
        model.Timeline.Skip(1).All(t => t.Kind == "event").Should().BeTrue();
    }

    private async Task AppendOneEvent()
    {
        var aggregate = FooAggregate.Open("Hello");
        await plumber.SaveNew(aggregate);
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
