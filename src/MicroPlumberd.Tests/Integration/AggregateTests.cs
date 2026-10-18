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
            using var scope = OperationContext.GetOrCreate(Flow.Component);
            scope.Context.SetCausationId(Guid.NewGuid()).SetCorrelationId(Guid.NewGuid()).SetUserId(Guid.NewGuid().ToString());

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
        /// <summary>
        /// Issue #25 (rev-reach's reproduction): a stream holding an event of a type this build has NO mapping for — what a
        /// newer release's event is to an older one after a rollback. The version is the stream revision, so the next save's
        /// expected version is right.
        /// </summary>
        [Fact]
        public async Task AnUnmappedEventInTheStream_CountsForVersion_AndTheNextSaveSucceeds()
        {
            await es.StartInDocker();
            var aggregate = FooAggregate.Open("Hello");
            await plumber.SaveNew(aggregate);

            var streamId = plumber.Config.Conventions.GetStreamIdConvention(OperationContext.GetOrCreate(Flow.Component).Context,
                typeof(FooAggregate), aggregate.Id);
            await using (var client = new KurrentDB.Client.KurrentDBClient(es.GetEventStoreSettings()))
                await client.AppendToStreamAsync(streamId, KurrentDB.Client.StreamState.StreamExists,
                [new KurrentDB.Client.EventData(KurrentDB.Client.Uuid.NewUuid(), "FooFromANewerRelease",
                    System.Text.Encoding.UTF8.GetBytes("""{"Whatever":1}"""))]);

            var reread = await plumber.Get<FooAggregate>(aggregate.Id);

            reread.Version.Should().Be(1, "two events in the stream — the unmapped one counts for version");
            reread.State.Name.Should().Be("Hello", "and is skipped for state");

            reread.Refine("World");
            await plumber.SaveChanges(reread);   // threw WrongExpectedVersionException before the fix

            (await plumber.Get<FooAggregate>(aggregate.Id)).Should().Match<FooAggregate>(a => a.Version == 2 && a.State.Name == "World");
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