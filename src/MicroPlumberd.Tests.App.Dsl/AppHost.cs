using BoDi;
using MicroPlumberd.Services;
using MicroPlumberd.Testing;
using MicroPlumberd.Tests.App.Domain;
using MicroPlumberd.Tests.App.Infrastructure;
using MicroPlumberd.Tests.App.Srv;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
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
                .AddSingleton<InMemoryAssertionDb>()
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


    /// <summary>How long <see cref="StartAsync"/> waits for the handlers to be ready before it THROWS.</summary>
    public TimeSpan ReadyTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Starts the host and waits until the MicroPlumberd handlers are ready — BOUNDED. It was <c>while (true)</c> on the health
    /// check: a starter that threw (the host then stops; IsReady never turns true) or stalled spun the test forever, silently —
    /// a full suite killed by its timeout with no summary and the store left running (the test's finally never ran). Now:
    /// a host that STOPPED (a faulted starter, BackgroundServiceExceptionBehavior.StopHost) or a health result carrying an
    /// exception throws at once; one that never becomes ready throws after <see cref="ReadyTimeout"/> with the health text.
    /// </summary>
    public async Task<IServiceProvider> StartAsync()
    {
        // Host.StartAsync itself is bounded too (rev-reach 23166defc): a hosted service blocking in ITS StartAsync would
        // otherwise hang one line before the readiness loop.
        try { await Host.StartAsync().WaitAsync(ReadyTimeout); }
        catch (TimeoutException)
        {
            throw new TimeoutException($"The test host's Host.StartAsync did not return within {ReadyTimeout.TotalSeconds:0.#} s — a hosted service is blocking in its StartAsync.");
        }

        var hc = Host.Services.GetRequiredService<StartupHealthCheck>();
        var lifetime = Host.Services.GetRequiredService<IHostApplicationLifetime>();
        var deadline = DateTime.UtcNow + ReadyTimeout;
        while (true)
        {
            var ret = await hc.CheckHealthAsync(null, default);
            if (ret.Status == HealthStatus.Healthy) break;
            if (ret.Exception != null || lifetime.ApplicationStopping.IsCancellationRequested)
                throw new InvalidOperationException($"The test host stopped before it was ready: {ret.Description}", ret.Exception);
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException($"The test host was not ready after {ReadyTimeout.TotalSeconds:0.#} s: {ret.Description}");
            await Task.Delay(100);
        }

        return Host.Services;
    }
}