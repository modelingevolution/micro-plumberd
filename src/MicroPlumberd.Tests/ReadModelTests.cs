using EventStore.Client;
using FluentAssertions;
using MicroPlumberd.Tests.Fixtures;

namespace MicroPlumberd.Tests;

public class ReadModel_IntegrationTests : IClassFixture<EventStoreServer>
{
    private readonly EventStoreServer _eventStore;

    private readonly IPlumber plumber;
   
    public ReadModel_IntegrationTests(EventStoreServer eventStore)
    {
        _eventStore = eventStore;
        plumber = new Plumber(_eventStore.GetEventStoreSettings());
    }
    
    

    [Fact]
    public async Task SubscribeModel()
    {
        await _eventStore.StartInDocker();
        await AppendOneEvent();

        var fooModel = new AppSrc.FooModel();

        var sub= await plumber.SubscribeModel(fooModel);
        
        await Task.Delay(1000);

        fooModel.Index.Should().HaveCount(1);
    }

    private async Task AppendOneEvent()
    {
        AppSrc.FooAggregate aggregate = AppSrc.FooAggregate.New(Guid.NewGuid());
        aggregate.Open("Hello");
        await plumber.SaveNew(aggregate);
    }
}