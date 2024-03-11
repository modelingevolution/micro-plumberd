using EventStore.Client;
using FluentAssertions;

namespace MicroPlumberd.Tests;

public class ReadModelTests
{
    private static EventStoreClientSettings GetEventStoreSettings()
    {
        const string connectionString = "esdb://admin:changeit@localhost:2113?tls=false&tlsVerifyCert=false";

        return EventStoreClientSettings.Create(connectionString);
    }
    private readonly IPlumber plumber;

    public ReadModelTests()
    {
        plumber = new Plumber(GetEventStoreSettings());
    }

    [Fact]
    public async Task SubscribeModel()
    {
        var fooModel = new FooModel();

        var sub= await plumber.SubscribeModel(fooModel);
        
        await Task.Delay(1000);

        fooModel.Index.Should().HaveCountGreaterThan(0);
    }
}