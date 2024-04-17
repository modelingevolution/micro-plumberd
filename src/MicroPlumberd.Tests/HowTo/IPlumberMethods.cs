using EventStore.Client;
using FluentAssertions;
using MicroPlumberd.Testing;
using MicroPlumberd.Tests.App.Domain;
using MicroPlumberd.Tests.HowTo.CinemaExample;
using MicroPlumberd.Tests.Utils;
using Microsoft.Identity.Client;

namespace MicroPlumberd.Tests.HowTo
{
    [TestCategory("MethodsExample")]
    public class PlumberMethodsTests : IClassFixture<EventStoreServer>
    {
        private readonly IPlumber plumber;
        private readonly EventStoreServer es;


        public PlumberMethodsTests(EventStoreServer es)
        {
            plumber = Plumber.Create(es.GetEventStoreSettings());
            this.es = es;
        }
        [Fact]
        public async Task HowToAppendEventToHisBaseStream()
        {
            await RunEventStoreForTestPurpose();

            var ourLovelyEvent = new TicketReserved();
            var suffixOfStreamWhereOurEventWillBeAppend = Guid.NewGuid();
            await plumber.AppendEvent(ourLovelyEvent, suffixOfStreamWhereOurEventWillBeAppend);
        }

        [Fact]
        public async Task HowToAppendEventToSpecificStream()
        {
            await RunEventStoreForTestPurpose();

            var streamIdentifier = Guid.NewGuid();
            var ourLovelyEvent = new TicketReserved();

            await plumber.AppendEvent(ourLovelyEvent, $"VIPReservationStream-{streamIdentifier}");

        }

       
        [Fact]
        public async Task HowToMakeModelSubscribe()
        {
            await RunEventStoreForTestPurpose();

            var fromWhenShouldWeSubscribeOurStream = FromRelativeStreamPosition.Start;
            var modelThatWantToSubscribeToStream = new ReservationModel(new InMemoryModelStore());
          

            await plumber.SubscribeEventHandler(modelThatWantToSubscribeToStream, start: fromWhenShouldWeSubscribeOurStream);

            var suffixOfStreamWhereOurEventWillBeAppend = Guid.NewGuid();
            var ourLovelyEvent = new TicketReserved();

            await plumber.AppendEvent(ourLovelyEvent, suffixOfStreamWhereOurEventWillBeAppend);
            await Task.Delay(1000);

            modelThatWantToSubscribeToStream.EventHandled.Should().BeTrue();
          
        }

        [Fact]
        public async Task HowToMakeModelSubscribeFromLastEvent()
        {
            await RunEventStoreForTestPurpose();

            var modelThatWantToSubscribeToStream = new ReservationModel(new InMemoryModelStore());
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
            await RunEventStoreForTestPurpose();

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
            await RunEventStoreForTestPurpose();

            var suffixOfStreamWhereOurEventWillBeAppend = Guid.NewGuid();
            var ourLovelyEvent = new TicketReserved();

            await plumber.AppendEvent( ourLovelyEvent, suffixOfStreamWhereOurEventWillBeAppend);
            
            //await plumber.FindEventInStream($"ReservationStream-{streamIdentifier}", ourLovelyEvent);


        }
        [Fact]
        public async Task HowToRehydrateModel()
        {
            await RunEventStoreForTestPurpose();
            var modelThatWantToSubscribeToStream = new ReservationModel(new InMemoryModelStore());
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

        private async Task RunEventStoreForTestPurpose()
        {
            await es.StartInDocker();
        }

    }
}
