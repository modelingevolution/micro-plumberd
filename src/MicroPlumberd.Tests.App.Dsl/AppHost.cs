using BoDi;
using MicroPlumberd.Services;
using MicroPlumberd.Testing;
using MicroPlumberd.Tests.App.Domain;
using MicroPlumberd.Tests.App.Srv;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TechTalk.SpecFlow;
using Xunit.Abstractions;

[assembly: DslFromAssembly("MicroPlumberd.Tests.App")]

[Binding]
public class AppSteps
{
    private readonly AppStepsContext _context;
    

    public AppSteps(AppStepsContext context)
    {
        _context = context;
    }
    


    [Given(@"the Foo App is up and running")]
    public async Task GivenTheAppIsUpAndRunning()
    {
        _context.EventStore = await EventStoreServer.Create().StartInDocker();

        var testAppHost = new TestAppHost(_context.Output)
            .Configure(x => x
                .AddPlumberd(_context.EventStore.GetEventStoreSettings())
                .AddCommandHandler<FooCommandHandler>()
                .AddSingleton<InMemoryModelStore>()
                .AddEventHandler<FooModel>());
        _context.App = testAppHost.Host;

        await testAppHost.StartAsync();
    }

}
public partial class TestAppHost : IDisposable
{
    public IHost Host { get; private set; }
    private readonly ITestOutputHelper logger;

    public TestAppHost(ITestOutputHelper logger)
    {
        this.logger = logger;
    }

    public virtual TestAppHost Configure(Action<IServiceCollection>? configure = null)
    {
        Host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .ConfigureLogging(x => x.SetMinimumLevel(LogLevel.Trace)
                .AddDebug()
                .AddXunit(logger))
            .ConfigureServices(services =>
            {
                configure(services);
            })
            .Build();

        return this;
    }

    public void Dispose()
    {
        Host?.Dispose();
    }


    public async Task<IServiceProvider> StartAsync()
    {
        await Host.StartAsync();
        await Task.Delay(1000);
        return Host.Services;
    }
}