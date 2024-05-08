using MicroPlumberd.Services;

using MicroPlumberd.Tests.Utils;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using MicroPlumberd.Testing;
using MicroPlumberd.Tests.App.Domain;
using MicroPlumberd.Tests.App.Srv;
using ModelingEvolution.DirectConnect;
using Xunit.Sdk;
using Xunit.Abstractions;

namespace MicroPlumberd.Tests.Integration.Services;

[TestCategory("Integration")]
public class ProcessManagerTests : IClassFixture<EventStoreServer>
{
    private readonly EventStoreServer _eventStore;
    private readonly TestAppHost _serverTestApp;
    private readonly TestAppHost _clientTestApp;
    public ProcessManagerTests(EventStoreServer eventStore, ITestOutputHelper testOutputHelper)
    {
        _eventStore = eventStore;
        _serverTestApp = new TestAppHost(testOutputHelper);
        _clientTestApp = new TestAppHost(testOutputHelper);
    }

    [Fact]
    public async Task TestProcessorFlow()
    {
        await _eventStore.StartInDocker();

        await _serverTestApp.Configure(x => x
            .AddPlumberd(_eventStore.GetEventStoreSettings(), (sp, x) => x.ServicesConfig().DefaultTimeout = TimeSpan.FromSeconds(100))
            .AddCommandHandler<FooCommandHandler>()
            .AddCommandHandler<BooCommandHandler>()
            .AddProcessManager<XooProcessManager>())
            .StartAsync();

        await Task.Delay(2000);

        
        var client = await _clientTestApp.Configure(x=> x
            .AddPlumberd(_eventStore.GetEventStoreSettings(), (sp, x) => x.ServicesConfig().DefaultTimeout = TimeSpan.FromSeconds(100)))
            .StartAsync();
        var clientBus = client.GetRequiredService<ICommandBus>();

        Stopwatch sw = new Stopwatch();
        sw.Start();
        await clientBus.SendAsync(Guid.NewGuid(), new CreateFoo() { Name = "Hello" });
        sw.Stop();

        
        await Task.Delay(2000);
        await clientBus.SendAsync("Hello".ToGuid(), new RefineBoo() { Name="Okidoki"} );

        await Task.Delay(2000);
    }
}