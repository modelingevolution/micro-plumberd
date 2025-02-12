using System.Reflection;
using FluentAssertions;
using MicroPlumberd.Testing;
using MicroPlumberd.Tests.App.Domain;

using MicroPlumberd.Tests.Utils;

using TechTalk.SpecFlow.UnitTestProvider;

namespace MicroPlumberd.Tests.Integration
{
    [TestCategory("Unit")]
    public class AggregateReflection
    {
        [Fact]
        public void GivenMethodsAreAccessible()
        {
            var mth = typeof(FooAggregate).GetMethods(BindingFlags.NonPublic | BindingFlags.InvokeMethod | BindingFlags.Instance)
                .Where(x=>x.Name == "Given").FirstOrDefault();

            var acceptedTypeAttributes = mth.GetCustomAttributes<AcceptedTypeAttribute>().ToArray();
            acceptedTypeAttributes.Should().HaveCountGreaterThan(1);
        }
    }
   

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

            FooAggregate aggregate = FooAggregate.Open("Hello");

            await plumber.SaveNew(aggregate);
        }
        [Fact]
        public async Task Get()
        {
            await es.StartInDocker();
            FooAggregate aggregate = FooAggregate.Open("Hello");

            await plumber.SaveNew(aggregate);

            var aggregate2 = await plumber.Get<FooAggregate>(aggregate.Id);

            aggregate2.Version.Should().Be(0);
            aggregate2.State.Name.Should().Be("Hello");
        }
        [Fact]
        public async Task Update()
        {
            await es.StartInDocker();
            FooAggregate aggregate = FooAggregate.Open("Hello");
            await plumber.SaveNew(aggregate);

            aggregate.Refine("World");

            await plumber.SaveChanges(aggregate);
        }

    }

}