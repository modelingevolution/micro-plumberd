using System.Data.Common;
using EventStore.Client;
using FluentAssertions;
using MicroPlumberd;
using MicroPlumberd.Tests.Fixtures;
using MicroPlumberd.Tests.AppSrc;

namespace MicroPlumberd.Tests
{
    
    public class Aggregate_IntegrationTests : IClassFixture<EventStoreServer>
    {
        private readonly IPlumber plumber;
        private readonly EventStoreServer es;

        
        public Aggregate_IntegrationTests(EventStoreServer es)
        {
            plumber = new Plumber(es.GetEventStoreSettings());
            this.es  = es;
        }

        [Fact]
        public async Task New()
        {
            await es.StartInDocker();
            using var scope = new InvocationScope();
            scope.SetCausation(Guid.NewGuid()).SetCorrelation(Guid.NewGuid()).SetUserId(Guid.NewGuid());

            FooAggregate aggregate = FooAggregate.New(Guid.NewGuid());
            aggregate.Open("Hello");

            await plumber.SaveNew(aggregate);
        }
        [Fact]
        public async Task Get()
        {
            await es.StartInDocker();
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
            await es.StartInDocker();
            FooAggregate aggregate = FooAggregate.New(Guid.NewGuid());
            aggregate.Open("Hello");
            await plumber.SaveNew(aggregate);

            aggregate.Change("World");

            await plumber.SaveChanges(aggregate);
        }
        
    }

}