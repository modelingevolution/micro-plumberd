using EventStore.Client;
using FluentAssertions;
using LiteDB;
using MicroPlumberd.Services;
using MicroPlumberd.Testing;

using MicroPlumberd.Tests.App.CinemaDomain;
using MicroPlumberd.Tests.App.Domain;
using MicroPlumberd.Tests.App.Infrastructure;

using MicroPlumberd.Tests.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Identity.Client;
using Xunit.Abstractions;

namespace MicroPlumberd.Tests.HowTo
{
    [TestCategory("HowTo")]
    public class PlumberMethodsTests : IClassFixture<EventStoreServer>, IDisposable
    {
        private readonly IPlumber plumber;
        private readonly EventStoreServer es;
        private readonly TestAppHost _host;

        public PlumberMethodsTests(EventStoreServer es, ITestOutputHelper logger)
        {
            plumber = Plumber.Create(es.GetEventStoreSettings());
            _host = new TestAppHost(logger);
            this.es = es;
        }
        [Fact]
        public async Task HowToAppendEventToHisBaseStream()
        {
            await es.StartInDocker();

            var ourLovelyEvent = new TicketReserved();
            var suffixOfStreamWhereOurEventWillBeAppend = Guid.NewGuid();
            await plumber.AppendEvent(ourLovelyEvent, suffixOfStreamWhereOurEventWillBeAppend);
        }

        [Fact]
        public async Task HowToAppendEventToSpecificStream()
        {
            await es.StartInDocker();

            var streamIdentifier = Guid.NewGuid();
            var ourLovelyEvent = new TicketReserved();

            await plumber.AppendEventToStream($"VIPReservationStream-{streamIdentifier}", ourLovelyEvent);

        }

       
        [Fact]
        public async Task HowToMakeModelSubscribe()
        {
            await es.StartInDocker();

            var fromWhenShouldWeSubscribeOurStream = FromRelativeStreamPosition.Start;
            var modelThatWantToSubscribeToStream = new ReservationModel(new InMemoryAssertionDb());
          

            await plumber.SubscribeEventHandler(modelThatWantToSubscribeToStream, start: fromWhenShouldWeSubscribeOurStream);

            var suffixOfStreamWhereOurEventWillBeAppend = Guid.NewGuid();
            var ourLovelyEvent = new TicketReserved();

            await plumber.AppendEvent(ourLovelyEvent, suffixOfStreamWhereOurEventWillBeAppend);
            await Task.Delay(1000);

            modelThatWantToSubscribeToStream.EventHandled.Should().BeTrue();
          
        }

        [Fact]
        public async Task HowToSubscribeRealModel()
        {
            await es.StartInDocker();

            using var db = LiteDbFactory.Get();
            var dbModel = new DbReservationModel(LiteDbFactory.Get());

            await plumber.SubscribeEventHandler(dbModel);

            var suffixOfStreamWhereOurEventWillBeAppend = Guid.NewGuid();
            var ourLovelyEvent = new TicketReserved() { MovieName = "Golden Eye", RoomName = "101"};

            await plumber.AppendEvent(ourLovelyEvent, suffixOfStreamWhereOurEventWillBeAppend);
            await Task.Delay(1000);

            dbModel.Reservations.Query().Count().Should().Be(1);

        }
        [Fact]
        public async Task HowToSubscribeRealModelWithHost()
        {
            await es.StartInDocker();

            var sp = await _host.Configure(x => x
                    .AddSingleton<LiteDatabase>((sp) => LiteDbFactory.Get())
                    .AddEventHandler<DbReservationModel>(true)
                    .AddPlumberd(es.GetEventStoreSettings()))
                .StartAsync();
            
            var suffixOfStreamWhereOurEventWillBeAppend = Guid.NewGuid();
            var ourLovelyEvent = new TicketReserved() { MovieName = "Golden Eye", RoomName = "101" };

            await sp.GetRequiredService<IPlumber>().AppendEvent(ourLovelyEvent, suffixOfStreamWhereOurEventWillBeAppend);
            await Task.Delay(1000);

            sp.GetRequiredService<LiteDatabase>().Reservations().Query().Count().Should().Be(1);

        }

        [Fact]
        public async Task HowToMakeModelSubscribeFromLastEvent()
        {
            await es.StartInDocker();

            var modelThatWantToSubscribeToStream = new ReservationModel(new InMemoryAssertionDb());
            var suffixOfStreamWhereOurEventWillBeAppend = Guid.NewGuid();
            var someVeryOldEvent = new TicketReserved();

            await plumber.AppendEvent(someVeryOldEvent, suffixOfStreamWhereOurEventWillBeAppend);
            await Task.Delay(1000);

            await plumber.SubscribeEventHandler(modelThatWantToSubscribeToStream, start: FromRelativeStreamPosition.End-1);
            modelThatWantToSubscribeToStream.EventHandled.Should().BeFalse();

            var ourLovelyEvent = new TicketReserved();

            await plumber.AppendEvent(ourLovelyEvent, suffixOfStreamWhereOurEventWillBeAppend);
            await Task.Delay(1000);
            modelThatWantToSubscribeToStream.EventHandled.Should().BeTrue();


        }
        [Fact]
        public async Task HowToLinkEventsToOtherStream()
        {
            await es.StartInDocker();

            var ourLovelyEvent = new TicketReserved()
            {
                MovieName = "Scream",
                RoomName = "Venus"
            };
            await plumber.SubscribeEventHandlerPersistently(new TicketProjection(plumber));
            await plumber.AppendEvent(ourLovelyEvent);

            await plumber.Subscribe($"RoomOccupancy-{ourLovelyEvent.RoomName}", FromRelativeStreamPosition.Start)
                .WithHandler(new RoomOccupancy());


        }
        [Fact]
        public async Task HowToFindEventInStream()
        {
            await es.StartInDocker();

            var suffixOfStreamWhereOurEventWillBeAppend = Guid.NewGuid();
            var ourLovelyEvent = new TicketReserved();

            await plumber.AppendEvent( ourLovelyEvent, suffixOfStreamWhereOurEventWillBeAppend);
            
            //await plumber.FindEventInStream($"ReservationStream-{streamIdentifier}", ourLovelyEvent);


        }
        [Fact]
        public async Task HowToRehydrateModel()
        {
            await es.StartInDocker();
            var modelThatWantToSubscribeToStream = new ReservationModel(new InMemoryAssertionDb());
            var suffixOfStreamWhereOurEventWillBeAppend = Guid.NewGuid();
            var ourLovelyEvent = new TicketReserved();

            await plumber.AppendEvent( ourLovelyEvent, suffixOfStreamWhereOurEventWillBeAppend);
            await Task.Delay(1000);

            await plumber.SubscribeEventHandler(modelThatWantToSubscribeToStream, start: FromRelativeStreamPosition.Start);

            await Task.Delay(1000);
            modelThatWantToSubscribeToStream.EventHandled.Should().BeTrue();

            modelThatWantToSubscribeToStream.EventHandled = false;
            modelThatWantToSubscribeToStream.EventHandled.Should().BeFalse();

            await plumber.Rehydrate(modelThatWantToSubscribeToStream,
                $"ReservationStream-{suffixOfStreamWhereOurEventWillBeAppend}");

            modelThatWantToSubscribeToStream.EventHandled.Should().BeTrue();

        }


        public void Dispose()
        {
            _host?.Dispose();
        }
    }
}
