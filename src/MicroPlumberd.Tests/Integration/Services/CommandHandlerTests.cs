using MicroPlumberd.Tests.Utils;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Docker.DotNet.Models;
using EventStore.Client;
using MicroPlumberd.Services;

using FluentAssertions;
using MicroPlumberd.Protobuf;
using MicroPlumberd.Testing;
using ModelingEvolution.DirectConnect;
using Xunit.Abstractions;
using MicroPlumberd.Tests.App.Domain;
using MicroPlumberd.Tests.App.Infrastructure;
using MicroPlumberd.Tests.App.Srv;
using Xunit.Sdk;
using static MicroPlumberd.Tests.Integration.Services.FluentAssertionExtensions;

namespace MicroPlumberd.Tests.Integration.Services
{
    public static class FluentAssertionExtensions
    {
        public static async Task Eventually(
            Func<Task> action,
            TimeSpan? timeout = null,
            TimeSpan? interval = null)
        {
            timeout ??= TimeSpan.FromSeconds(5);
            interval ??= TimeSpan.FromMilliseconds(100);

            var sw = Stopwatch.StartNew();
            Exception? lastException = null;

            while (sw.Elapsed < timeout)
            {
                try
                {
                    await action();
                    return;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    await Task.Delay(interval.Value);
                }
            }

            throw new XunitException($"Assertion failed within timeout of {timeout}. Last error: {lastException}");
        }
        public static async Task Eventually(
            Action action,
            TimeSpan? timeout = null,
            TimeSpan? interval = null)
        {
            timeout ??= TimeSpan.FromSeconds(5);
            interval ??= TimeSpan.FromMilliseconds(100);

            var sw = Stopwatch.StartNew();
            Exception? lastException = null;

            while (sw.Elapsed < timeout)
            {
                try
                {
                    action();
                    return;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    await Task.Delay(interval.Value);
                }
            }

            throw new XunitException($"Assertion failed within timeout of {timeout}. Last error: {lastException}");
        }
    }
    
    [TestCategory("Integration")]
    public class CommandHandlerTests : IClassFixture<EventStoreServer>, IAsyncLifetime
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
        public async Task HandleAggregateCommandsWithProtoSerialization()
        {
            
            _serverTestApp.Configure(x => x
                .AddPlumberd(_eventStore.GetEventStoreSettings(), (sp,x)=>x.SerializerFactory = x => new ProtoBuffObjectSerialization())
                .AddCommandHandler<BooCommandHandler>(start: StreamPosition.Start));

            var srv = await _serverTestApp.StartAsync();
            

            var client = await _clientTestApp.Configure(x => x
                    .AddPlumberd(_eventStore.GetEventStoreSettings(), (sp, x) =>
                    {
                        x.ServicesConfig().DefaultTimeout = TimeSpan.FromSeconds(10);
                        x.SerializerFactory = x => new ProtoBuffObjectSerialization();
                    }))
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
        public async Task HandleAggregateCommands()
        {
            
            _serverTestApp.Configure(x => x
                .AddPlumberd(_eventStore.GetEventStoreSettings())
                .AddCommandHandler<BooCommandHandler>(start: StreamPosition.Start));

            var srv = await _serverTestApp.StartAsync();
            
            var client = await _clientTestApp.Configure(x => x
                    .AddPlumberd(_eventStore.GetEventStoreSettings(), (sp, x) => x.ServicesConfig().DefaultTimeout = TimeSpan.FromSeconds(10)))
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
        public async Task HandleStrCommandHandle()
        {
            
            _serverTestApp.Configure(x => x
                .AddPlumberd(_eventStore.GetEventStoreSettings())
                .AddCommandHandler<StrCommandHandler>(start: StreamPosition.Start));

            var srv = await _serverTestApp.StartAsync();

            var client = await _clientTestApp.Configure(x => x
                    .AddPlumberd(_eventStore.GetEventStoreSettings(), (sp, x) => x.ServicesConfig().DefaultTimeout = TimeSpan.FromSeconds(10)))
                .StartAsync();

            var bus = client.GetRequiredService<ICommandBus>();

            await bus.SendAsync("Fun", new CreateStrFoo() { Name = "Cool" });

            var state = await srv.GetRequiredService<IPlumber>().GetState<StrEntityState>("Fun");
            state.Should().NotBeNull();
        }
        [Fact]
        public async Task HandleCommandsScoped()
        {
            
            _serverTestApp.Configure(x => x
                .AddPlumberd(_eventStore.GetEventStoreSettings(), scopedCommandBus:true)
                .AddCommandHandler<FooCommandHandler>(start: StreamPosition.Start));

            var srv = await _serverTestApp.StartAsync();

            var fooModel = new FooModel(new InMemoryAssertionDb());
            var sub = await srv.GetRequiredService<IPlumber>().SubscribeEventHandler(fooModel);

            var client = await _clientTestApp.Configure(x => x
                    .AddPlumberd(_eventStore.GetEventStoreSettings(), (sp, x) => x.ServicesConfig().DefaultTimeout = TimeSpan.FromSeconds(5), scopedCommandBus:true))
                .StartAsync();

            using var scope = client.CreateScope();
            var bus = scope.ServiceProvider.GetRequiredService<ICommandBus>();

            Stopwatch sw = new Stopwatch();
            sw.Start();
            await Parallel.ForAsync(0, 100, new ParallelOptions() { MaxDegreeOfParallelism = 10 },
                async (i, ct) => await bus.QueueAsync(Guid.NewGuid(), new CreateFoo() { Name = $"Name_{i}", TimeoutMs = 100 }, token: ct, fireAndForget: false));

            _testOutputHelper.WriteLine("Command executed in: " + sw.Elapsed / 100);

            await Eventually(() => fooModel.AssertionDb.Index.Should().HaveCount(100));

        }
        [Fact]
        public async Task HandleCommandsParallel()
        {
            
            _serverTestApp.Configure(x => x
                .AddPlumberd(_eventStore.GetEventStoreSettings())
                .AddCommandHandler<FooCommandHandler>(start: StreamPosition.Start));

            var srv = await _serverTestApp.StartAsync();

            var fooModel = new FooModel(new InMemoryAssertionDb());
            var sub = await srv.GetRequiredService<IPlumber>().SubscribeEventHandler(fooModel);

            var client = await _clientTestApp.Configure(x => x
                    .AddPlumberd(_eventStore.GetEventStoreSettings(), (sp, x) => x.ServicesConfig().DefaultTimeout = TimeSpan.FromSeconds(5)))
                .StartAsync();

            var bus = client.GetRequiredService<ICommandBus>();

            Stopwatch sw = new Stopwatch();
            sw.Start();
            await Parallel.ForAsync(0, 100, new ParallelOptions() { MaxDegreeOfParallelism = 10 },
                 async (i, ct) => await bus.QueueAsync(Guid.NewGuid(), new CreateFoo() { Name = $"Name_{i}", TimeoutMs = 100 }, token: ct, fireAndForget:false));

            _testOutputHelper.WriteLine("Command executed in: " + sw.Elapsed / 100);

            await Eventually(() => fooModel.AssertionDb.Index.Should().HaveCount(100));

        }
        [Fact]
        public async Task HandleCommands()
        {
            
            _serverTestApp.Configure(x => x
                .AddPlumberd(_eventStore.GetEventStoreSettings())
                .AddCommandHandler<FooCommandHandler>(start: StreamPosition.Start));

            var srv = await _serverTestApp.StartAsync();
            
            var fooModel = new FooModel(new InMemoryAssertionDb());
            var sub = await srv.GetRequiredService<IPlumber>().SubscribeEventHandler(fooModel);

            var client = await _clientTestApp.Configure(x => x
                    .AddPlumberd(_eventStore.GetEventStoreSettings(), (sp, x) => x.ServicesConfig().DefaultTimeout = TimeSpan.FromSeconds(5)))
                .StartAsync();

            var bus = client.GetRequiredService<ICommandBus>();

            Stopwatch sw = new Stopwatch();
            sw.Start();
            for (int i = 0; i < 100; i++) 
                 await bus.SendAsync(Guid.NewGuid(), new CreateFoo() { Name = $"Name_{i}", TimeoutMs = 100 });

            _testOutputHelper.WriteLine("Command executed in: " + sw.Elapsed/100);

            await Eventually(() => fooModel.AssertionDb.Index.Should().HaveCount(100));

        }
        [Fact]
        public async Task HandleCommand()
        {
            
            _serverTestApp.Configure(x => x
                .AddPlumberd(_eventStore.GetEventStoreSettings())
                .AddCommandHandler<FooCommandHandler>(start: StreamPosition.Start));

            var srv = await _serverTestApp.StartAsync();
            
            var cmd = new CreateFoo() { Name = "Hello" };
            var recipientId = Guid.NewGuid();

            Stopwatch sw = new Stopwatch();
            sw.Start();

            var client = await _clientTestApp.Configure( x=>x
                .AddPlumberd(_eventStore.GetEventStoreSettings(), (sp, x) => x.ServicesConfig().DefaultTimeout = TimeSpan.FromSeconds(10)))
                .StartAsync();

            await client.GetRequiredService<ICommandBus>().SendAsync(recipientId, cmd);

            _testOutputHelper.WriteLine("Command executed in: " + sw.Elapsed);
            

            var fooModel = new FooModel(new InMemoryAssertionDb());
            var sub = await srv.GetRequiredService<IPlumber>().SubscribeEventHandler(fooModel);
            await Eventually(() =>
            {

                fooModel.AssertionDb.Index.Should().HaveCount(1);
                fooModel.AssertionDb.Index[0].Metadata.CausationId().Should().Be(cmd.Id);
                fooModel.AssertionDb.Index[0].Metadata.CorrelationId().Should().Be(cmd.Id);
                fooModel.AssertionDb.Index[0].Metadata.SourceStreamId.Should().Be($"FooAggregate-{recipientId}");
            });
        }

        [Fact]
        public async Task HandleCommandException()
        {
            _serverTestApp.Configure(x => x
                .AddPlumberd(_eventStore.GetEventStoreSettings())
                .AddCommandHandler<FooCommandHandler>(start: StreamPosition.Start));
            

            var sp = await _serverTestApp.StartAsync();
            
            var cmd = new CreateFoo() { Name = "error" };
            
            Func<Task> action = async () => await sp.GetRequiredService<ICommandBus>().SendAsync(Guid.NewGuid(), cmd);

            await action.Should().ThrowAsync<FaultException<BusinessFault>>();
        }


        public async Task InitializeAsync()
        {
            await _eventStore.StartInDocker();
        }

        public async Task DisposeAsync()
        {
            await _eventStore.DisposeAsync();
        }
    }
}
