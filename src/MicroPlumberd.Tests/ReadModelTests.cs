using EventStore.Client;
using FluentAssertions;

namespace MicroPlumberd.Tests;

public class ReadModel_IntegrationTests
{
    private static EventStoreClientSettings GetEventStoreSettings()
    {
        string connectionString = $"esdb://admin:changeit@localhost:{EVENTSTORE_PORT}?tls=false&tlsVerifyCert=false";

        return EventStoreClientSettings.Create(connectionString);
    }
    private readonly IPlumber plumber;
    private const int EVENTSTORE_PORT = 2120;
    public ReadModel_IntegrationTests()
    {
        plumber = new Plumber(GetEventStoreSettings());

    }
    
    private async Task Init()
    {
        var es = new EventStoreServer();
        await es.StartInDocker(EVENTSTORE_PORT);
        await Task.Delay(8000);
    }

    [Fact]
    public async Task SubscribeModel()
    {
        await Init();
        await AppendOneEvent();

        var fooModel = new FooModel();

        var sub= await plumber.SubscribeModel(fooModel);
        
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