using MicroPlumberd.Tests.Fixtures;
using MicroPlumberd.Tests.Utils;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Docker.DotNet.Models;
using MicroPlumberd.Services;
using MicroPlumberd.Tests.AppSrc;
using FluentAssertions;
using ModelingEvolution.DirectConnect;

namespace MicroPlumberd.Tests.Integration.Services
{
    [TestCategory("Integration")]
    public class CommandHandlerTests : IClassFixture<EventStoreServer>
    {
        private readonly EventStoreServer _eventStore;
        private readonly App _serverApp;
        private readonly App _clientApp;

        public CommandHandlerTests(EventStoreServer eventStore)
        {
            _eventStore = eventStore;
            _serverApp = new App();
            _clientApp = new App();
        }

        [Fact]
        public async Task HandleCommand()
        {
            await _eventStore.StartInDocker();

            _serverApp.Configure(x => x
                .AddPlumberd(_eventStore.GetEventStoreSettings())
                .AddCommandHandler<FooCommandHandler>());

            var sp = await _serverApp.StartAsync();
            await Task.Delay(1000);
            var cmd = new CreateFoo() { Name = "Hello" };
            var recipientId = Guid.NewGuid();
            await sp.GetRequiredService<ICommandBus>().SendAsync(recipientId, cmd);
            
            var fooModel = new FooModel();
            var sub = await sp.GetRequiredService<IPlumber>().SubscribeEventHandle(fooModel);
            await Task.Delay(1000);

            fooModel.Index.Should().HaveCount(1);
            fooModel.Metadatas[0].CausationId().Should().Be(cmd.Id);
            fooModel.Metadatas[0].CorrelationId().Should().Be(cmd.Id);
            fooModel.Metadatas[0].SourceStreamId.Should().Be($"FooAggregate-{recipientId}");
        }

        [Fact]
        public async Task HandleCommandException()
        {
            await _eventStore.StartInDocker();

            _serverApp.Configure(x => x
                .AddPlumberd(_eventStore.GetEventStoreSettings())
                .AddCommandHandler<FooCommandHandler>());

            var sp = await _serverApp.StartAsync();
            
            var cmd = new CreateFoo() { Name = "error" };
            
            Func<Task> action = async () => await sp.GetRequiredService<ICommandBus>().SendAsync(Guid.NewGuid(), cmd);

            await action.Should().ThrowAsync<CommandFaultException>();
        }

       
    }
}
