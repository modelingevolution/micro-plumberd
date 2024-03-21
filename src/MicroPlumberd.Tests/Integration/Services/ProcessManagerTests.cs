using MicroPlumberd.Services;
using MicroPlumberd.Tests.AppSrc;
using MicroPlumberd.Tests.Fixtures;
using MicroPlumberd.Tests.Utils;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using ModelingEvolution.DirectConnect;
using Xunit.Sdk;
using Xunit.Abstractions;

namespace MicroPlumberd.Tests.Integration.Services;

[TestCategory("Integration")]
public class ProcessManagerTests : IClassFixture<EventStoreServer>
{
    private readonly EventStoreServer _eventStore;
    private readonly App _serverApp;
    private readonly App _clientApp;
    public ProcessManagerTests(EventStoreServer eventStore, ITestOutputHelper testOutputHelper)
    {
        _eventStore = eventStore;
        _serverApp = new App(testOutputHelper);
        _clientApp = new App(testOutputHelper);
    }

    [Fact]
    public async Task TestProcessorFlow()
    {
        await _eventStore.StartInDocker();

        await _serverApp.Configure(x => x
            .AddPlumberd(_eventStore.GetEventStoreSettings(), x => x.ServicesConfig().DefaultTimeout = TimeSpan.FromSeconds(100))
            .AddCommandHandler<FooCommandHandler>()
            .AddCommandHandler<BooCommandHandler>()
            .AddProcessManager<XooProcessManager>())
            .StartAsync();

        await Task.Delay(5000);

        
        var client = await _clientApp.Configure(x=> x
            .AddPlumberd(_eventStore.GetEventStoreSettings(), x => x.ServicesConfig().DefaultTimeout = TimeSpan.FromSeconds(100)))
            .StartAsync();
        var clientBus = client.GetRequiredService<ICommandBus>();

        Stopwatch sw = new Stopwatch();
        sw.Start();
        await clientBus.SendAsync(Guid.NewGuid(), new CreateFoo() { Name = "Hello" });
        sw.Stop();

        
        await Task.Delay(2000);
        await clientBus.SendAsync("Hello".ToGuid(), new ChangeBoo() { Name="Okidoki"} );


        await Task.Delay(30000);
    }
}