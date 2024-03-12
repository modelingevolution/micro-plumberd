using System.Data.Common;
using EventStore.Client;
using FluentAssertions;
using MicroPlumberd;

namespace MicroPlumberd.Tests
{
    
    public class Aggregate_IntegrationTests
    {
        private readonly IPlumber plumber;
        private const int EVENTSTORE_PORT = 2131;
        private async Task InitEventStore()
        {
            var es = new EventStoreServer();
            await es.StartInDocker(EVENTSTORE_PORT);
            await Task.Delay(8000);
        }
        public Aggregate_IntegrationTests()
        {
            plumber = new Plumber(GetEventStoreSettings());
        }

        [Fact]
        public async Task New()
        {
            await InitEventStore();
            await using var scope = new InvocationScope();
            scope.SetCausation(Guid.NewGuid()).SetCorrelation(Guid.NewGuid()).SetUserId(Guid.NewGuid());

            FooAggregate aggregate = FooAggregate.New(Guid.NewGuid());
            aggregate.Open("Hello");

            await plumber.SaveNew(aggregate);
        }
        [Fact]
        public async Task Get()
        {
            await InitEventStore();
            FooAggregate aggregate = FooAggregate.New(Guid.NewGuid());
            aggregate.Open("Hello");

            await plumber.SaveNew(aggregate);

            var aggregate2 = await plumber.Get<FooAggregate>(aggregate.Id);

            aggregate2.Age.Should().Be(0);
            aggregate2.State.Name.Should().Be("Hello");
        }
        [Fact]
        public async Task Update()
        {
            await InitEventStore();
            FooAggregate aggregate = FooAggregate.New(Guid.NewGuid());
            aggregate.Open("Hello");
            await plumber.SaveNew(aggregate);

            aggregate.Change("World");

            await plumber.SaveChanges(aggregate);
        }
        private static EventStoreClientSettings GetEventStoreSettings()
        {
            string connectionString = $"esdb://admin:changeit@localhost:{EVENTSTORE_PORT}?tls=false&tlsVerifyCert=false";

            return EventStoreClientSettings.Create(connectionString);
        }
    }

}