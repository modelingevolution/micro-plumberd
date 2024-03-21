using MicroPlumberd.Tests.Fixtures;
using MicroPlumberd.Tests.Utils;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Docker.DotNet.Models;
using MicroPlumberd.Services;
using MicroPlumberd.Tests.AppSrc;
using FluentAssertions;
using ModelingEvolution.DirectConnect;
using Xunit.Abstractions;

namespace MicroPlumberd.Tests.Integration.Services
{
    [TestCategory("Integration")]
    public class CommandHandlerTests : IClassFixture<EventStoreServer>
    {
        private readonly EventStoreServer _eventStore;
        private readonly ITestOutputHelper _testOutputHelper;
        private readonly App _serverApp;
        private readonly App _clientApp;

        public CommandHandlerTests(EventStoreServer eventStore, ITestOutputHelper testOutputHelper)
        {
            _eventStore = eventStore;
            _testOutputHelper = testOutputHelper;
            _serverApp = new App(testOutputHelper);
            _clientApp = new App(testOutputHelper);
        }

        [Fact]
        public async Task HandleAggregateCommands()
        {
            await _eventStore.StartInDocker();

            _serverApp.Configure(x => x
                .AddPlumberd(_eventStore.GetEventStoreSettings())
                .AddCommandHandler<BooCommandHandler>());

            var srv = await _serverApp.StartAsync();
            await Task.Delay(1000);

            
            var client = await _clientApp.Configure(x => x
                    .AddPlumberd(_eventStore.GetEventStoreSettings(), x => x.ServicesConfig().DefaultTimeout = TimeSpan.FromSeconds(5)))
                .StartAsync();

            var bus = client.GetRequiredService<ICommandBus>();

            Stopwatch sw = new Stopwatch();
            sw.Start();
            var recipientId = Guid.NewGuid();
            await bus.SendAsync(recipientId, new CreateBoo() { Name = $"Name1" });
            for (int i = 0; i < 100; i++)
                await bus.SendAsync(recipientId, new ChangeBoo() { Name = $"Name_{i}" });

            _testOutputHelper.WriteLine("Command executed in: " + sw.Elapsed / 101);

            await Task.Delay(3000);

        }

        [Fact]
        public async Task HandleCommands()
        {
            await _eventStore.StartInDocker();

            _serverApp.Configure(x => x
                .AddPlumberd(_eventStore.GetEventStoreSettings())
                .AddCommandHandler<FooCommandHandler>());

            var srv = await _serverApp.StartAsync();
            await Task.Delay(1000);
            
            var fooModel = new FooModel();
            var sub = await srv.GetRequiredService<IPlumber>().SubscribeEventHandler(fooModel);

            var client = await _clientApp.Configure(x => x
                    .AddPlumberd(_eventStore.GetEventStoreSettings(), x => x.ServicesConfig().DefaultTimeout = TimeSpan.FromSeconds(5)))
                .StartAsync();

            var bus = client.GetRequiredService<ICommandBus>();

            Stopwatch sw = new Stopwatch();
            sw.Start();
            for (int i = 0; i < 100; i++) 
                 bus.SendAsync(Guid.NewGuid(), new CreateFoo() { Name = $"Name_{i}"});

            _testOutputHelper.WriteLine("Command executed in: " + sw.Elapsed/100);
            
            await Task.Delay(3000);

            fooModel.Index.Should().HaveCount(100);
           
        }
        [Fact]
        public async Task HandleCommand()
        {
            await _eventStore.StartInDocker();

            _serverApp.Configure(x => x
                .AddPlumberd(_eventStore.GetEventStoreSettings())
                .AddCommandHandler<FooCommandHandler>());

            var srv = await _serverApp.StartAsync();
            await Task.Delay(1000);
            var cmd = new CreateFoo() { Name = "Hello" };
            var recipientId = Guid.NewGuid();

            Stopwatch sw = new Stopwatch();
            sw.Start();

            var client = await _clientApp.Configure( x=>x
                .AddPlumberd(_eventStore.GetEventStoreSettings(), x=> x.ServicesConfig().DefaultTimeout = TimeSpan.FromSeconds(5)))
                .StartAsync();

            await client.GetRequiredService<ICommandBus>().SendAsync(recipientId, cmd);

            _testOutputHelper.WriteLine("Command executed in: " + sw.Elapsed);
            

            var fooModel = new FooModel();
            var sub = await srv.GetRequiredService<IPlumber>().SubscribeEventHandler(fooModel);
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

            await action.Should().ThrowAsync<CommandFaultException<BusinessFault>>();
        }

       
    }
}
