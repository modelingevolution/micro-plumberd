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
using Microsoft.Extensions.Hosting;
using FluentAssertions;

namespace MicroPlumberd.Tests.Integration.Services
{
    public class App : IDisposable
    {
        private IHost host;

        public IHost Configure(Action<IServiceCollection>? configure = null)
        {
            host = Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    configure(services);
                })
                .Build();

            return host;
        }

        public void Dispose()
        {
            host?.Dispose();
        }


        public async Task<IServiceProvider> StartAsync()
        {
            await host.StartAsync();
            return host.Services;
        }
    }
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
            await sp.GetRequiredService<ICommandBus>().SendAsync(Guid.NewGuid(), new CreateFoo() { Name = "Hello" });
            
            await Task.Delay(30000);

            var fooModel = new FooModel();
            var sub = await sp.GetRequiredService<IPlumber>().SubscribeEventHandle(fooModel);
            await Task.Delay(1000);
            fooModel.Index.Should().HaveCount(1);
        }
    }
}
