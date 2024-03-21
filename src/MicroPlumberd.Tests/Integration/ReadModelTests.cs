using EventStore.Client;
using FluentAssertions;
using MicroPlumberd.Tests.AppSrc;
using MicroPlumberd.Tests.Fixtures;
using MicroPlumberd.Tests.Utils;

namespace MicroPlumberd.Tests.Integration;

[TestCategory("Integration")]
public class ReadModelTests : IClassFixture<EventStoreServer>
{
    private readonly EventStoreServer _eventStore;

    private readonly IPlumber plumber;

    public ReadModelTests(EventStoreServer eventStore)
    {
        _eventStore = eventStore;
        plumber = new Plumber(_eventStore.GetEventStoreSettings());
    }

    [Fact]
    public async Task SubscribeModelPersistently()
    {
        await _eventStore.StartInDocker();
        await AppendOneEvent();

        var fooModel = new FooModel();

        var sub = await plumber.SubscribeEventHandlerPersistently(fooModel, startFrom:StreamPosition.Start);

        await Task.Delay(1000);

        fooModel.Index.Should().HaveCount(1);
    }

    [Fact]
    public async Task SubscribeModel()
    {
        await _eventStore.StartInDocker();
        await AppendOneEvent();

        var fooModel = new FooModel();

        var sub = await plumber.SubscribeEventHandler(fooModel);

        await Task.Delay(1000);

        fooModel.Index.Should().HaveCount(1);
    }

    private async Task AppendOneEvent()
    {
        FooAggregate aggregate = FooAggregate.New(Guid.NewGuid());
        aggregate.Open("Hello");
        await plumber.SaveNew(aggregate);
    }
}