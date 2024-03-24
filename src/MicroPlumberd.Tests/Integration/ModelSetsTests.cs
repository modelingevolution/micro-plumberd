using EventStore.Client;
using FluentAssertions;
using MicroPlumberd.Tests.AppSrc;
using MicroPlumberd.Tests.Fixtures;
using MicroPlumberd.Tests.Utils;

namespace MicroPlumberd.Tests.Integration;

[TestCategory("Integration")]
public class ModelSetsTests : IClassFixture<EventStoreServer>
{
    private readonly EventStoreServer _eventStore;

    private readonly IPlumber plumber;

    public ModelSetsTests(EventStoreServer eventStore)
    {
        _eventStore = eventStore;
        plumber = new Plumber(_eventStore.GetEventStoreSettings());
    }


    [Fact]
    public async Task SubscribeModels()
    {
        await _eventStore.StartInDocker();
        var fk = Guid.NewGuid();
        await plumber.AppendEvents($"Dim-{fk}", StreamState.NoStream, new DimentionCreated() { Name = "Dependency" });
        await plumber.AppendEvents($"Fact-{Guid.NewGuid()}", StreamState.NoStream, new MasterRecordCreated() { Name = "Master", DependencyId = fk });

        var dimentionTable = new DimentionLookupModel();
        var factTable = new MasterModel(dimentionTable);


        await plumber.SubscribeSet()
            .With(dimentionTable)
            .With(factTable)
            .SubscribeAsync("MasterStream", FromStream.Start);

        await Task.Delay(1000);

        factTable.Index.Should().HaveCount(1);
        dimentionTable.Index.Should().HaveCount(1);
        factTable.Index.First().Value.DependencyName.Should().Be("Dependency");
    }

}

public class ProductionReplication
{
    [Fact]
    public async Task Replicate()
    {
        EventStoreServer srv = new EventStoreServer("Replicator");
        await srv.StartInDocker();
    }
}