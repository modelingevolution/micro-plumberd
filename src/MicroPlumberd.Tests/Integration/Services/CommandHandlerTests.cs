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

using FluentAssertions;
using MicroPlumberd.Testing;
using ModelingEvolution.DirectConnect;
using Xunit.Abstractions;
using MicroPlumberd.Tests.App.Domain;
using MicroPlumberd.Tests.App.Srv;


namespace MicroPlumberd.Tests.Integration.Services
{
    [TestCategory("Integration")]
    public class CommandHandlerTests : IClassFixture<EventStoreServer>
    {
        private readonly EventStoreServer _eventStore;
        private readonly ITestOutputHelper _testOutputHelper;
        private readonly TestAppHost _serverTestApp;
        private readonly TestAppHost _clientTestApp;

        public CommandHandlerTests(EventStoreServer eventStore, ITestOutputHelper testOutputHelper)
        {
            _eventStore = eventStore;
            _testOutputHelper = testOutputHelper;
            _serverTestApp = new TestAppHost(testOutputHelper);
            _clientTestApp = new TestAppHost(testOutputHelper);
        }

        [Fact]
        public async Task HandleAggregateCommands()
        {
            await _eventStore.StartInDocker();

            _serverTestApp.Configure(x => x
                .AddPlumberd(_eventStore.GetEventStoreSettings())
                .AddCommandHandler<BooCommandHandler>());

            var srv = await _serverTestApp.StartAsync();
            await Task.Delay(1000);

            
            var client = await _clientTestApp.Configure(x => x
                    .AddPlumberd(_eventStore.GetEventStoreSettings(), x => x.ServicesConfig().DefaultTimeout = TimeSpan.FromSeconds(120)))
                .StartAsync();

            var bus = client.GetRequiredService<ICommandBus>();

            Stopwatch sw = new Stopwatch();
            sw.Start();
            var recipientId = Guid.NewGuid();
            await bus.SendAsync(recipientId, new CreateBoo() { Name = $"Name1" });
            for (int i = 0; i < 1000; i++)
                await bus.SendAsync(recipientId, new RefineBoo() { Name = $"Name_{i}" });

            _testOutputHelper.WriteLine("Command executed in: " + sw.Elapsed / 101);

            await Task.Delay(3000);

        }

        [Fact]
        public async Task HandleCommands()
        {
            await _eventStore.StartInDocker();

            _serverTestApp.Configure(x => x
                .AddPlumberd(_eventStore.GetEventStoreSettings())
                .AddCommandHandler<FooCommandHandler>());

            var srv = await _serverTestApp.StartAsync();
            await Task.Delay(1000);
            
            var fooModel = new FooModel(new InMemoryModelStore());
            var sub = await srv.GetRequiredService<IPlumber>().SubscribeEventHandler(fooModel);

            var client = await _clientTestApp.Configure(x => x
                    .AddPlumberd(_eventStore.GetEventStoreSettings(), x => x.ServicesConfig().DefaultTimeout = TimeSpan.FromSeconds(5)))
                .StartAsync();

            var bus = client.GetRequiredService<ICommandBus>();

            Stopwatch sw = new Stopwatch();
            sw.Start();
            for (int i = 0; i < 100; i++) 
                 bus.SendAsync(Guid.NewGuid(), new CreateFoo() { Name = $"Name_{i}"});

            _testOutputHelper.WriteLine("Command executed in: " + sw.Elapsed/100);
            
            await Task.Delay(3000);

            fooModel.ModelStore.Index.Should().HaveCount(100);
           
        }
        [Fact]
        public async Task HandleCommand()
        {
            await _eventStore.StartInDocker();

            _serverTestApp.Configure(x => x
                .AddPlumberd(_eventStore.GetEventStoreSettings())
                .AddCommandHandler<FooCommandHandler>());

            var srv = await _serverTestApp.StartAsync();
            await Task.Delay(1000);
            var cmd = new CreateFoo() { Name = "Hello" };
            var recipientId = Guid.NewGuid();

            Stopwatch sw = new Stopwatch();
            sw.Start();

            var client = await _clientTestApp.Configure( x=>x
                .AddPlumberd(_eventStore.GetEventStoreSettings(), x=> x.ServicesConfig().DefaultTimeout = TimeSpan.FromSeconds(120)))
                .StartAsync();

            await client.GetRequiredService<ICommandBus>().SendAsync(recipientId, cmd);

            _testOutputHelper.WriteLine("Command executed in: " + sw.Elapsed);
            

            var fooModel = new FooModel(new InMemoryModelStore());
            var sub = await srv.GetRequiredService<IPlumber>().SubscribeEventHandler(fooModel);
            await Task.Delay(1000);

            fooModel.ModelStore.Index.Should().HaveCount(1);
            fooModel.ModelStore.Index[0].Metadata.CausationId().Should().Be(cmd.Id);
            fooModel.ModelStore.Index[0].Metadata.CorrelationId().Should().Be(cmd.Id);
            fooModel.ModelStore.Index[0].Metadata.SourceStreamId.Should().Be($"FooAggregate-{recipientId}");
        }

        [Fact]
        public async Task HandleCommandException()
        {
            await _eventStore.StartInDocker();

            _serverTestApp.Configure(x => x
                .AddPlumberd(_eventStore.GetEventStoreSettings())
                .AddCommandHandler<FooCommandHandler>());
            await Task.Delay(1000);

            var sp = await _serverTestApp.StartAsync();
            
            var cmd = new CreateFoo() { Name = "error" };
            
            Func<Task> action = async () => await sp.GetRequiredService<ICommandBus>().SendAsync(Guid.NewGuid(), cmd);

            await action.Should().ThrowAsync<FaultException<BusinessFault>>();
        }

       
    }
}
