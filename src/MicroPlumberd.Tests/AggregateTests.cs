using System.Data.Common;
using EventStore.Client;
using FluentAssertions;
using MicroPlumberd;

namespace MicroPlumberd.Tests
{
    
    public class AggregateTests
    {
        private readonly IPlumber plumber;

        public AggregateTests()
        {
            plumber = new Plumber(GetEventStoreSettings());

            
        }

        [Fact]
        public async Task New()
        {
            FooAggregate aggregate = FooAggregate.New(Guid.NewGuid());
            aggregate.Open("Hello");

            await plumber.SaveNew(aggregate);
        }
        [Fact]
        public async Task Get()
        {
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
            FooAggregate aggregate = FooAggregate.New(Guid.NewGuid());
            aggregate.Open("Hello");
            await plumber.SaveNew(aggregate);

            aggregate.Change("World");

            await plumber.SaveChanges(aggregate);
        }
        private static EventStoreClientSettings GetEventStoreSettings()
        {
            const string connectionString = "esdb://admin:changeit@localhost:2113?tls=false&tlsVerifyCert=false";

            return EventStoreClientSettings.Create(connectionString);
        }
    }
}