using FluentAssertions;
using MicroPlumberd.Tests.AppSrc;
using MicroPlumberd.Tests.Fixtures;
using MicroPlumberd.Tests.Utils;

namespace MicroPlumberd.Tests.Integration
{
    
  

    [TestCategory("Integration")]
    public class AggregateTests : IClassFixture<EventStoreServer>
    {
        private readonly IPlumber plumber;
        private readonly EventStoreServer es;


        public AggregateTests(EventStoreServer es)
        {
            plumber = Plumber.Create(es.GetEventStoreSettings());
            this.es = es;
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

            aggregate2.Version.Should().Be(0);
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