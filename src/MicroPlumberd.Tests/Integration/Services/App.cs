using Divergic.Logging.Xunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace MicroPlumberd.Tests.Integration.Services;

public class App : IDisposable
{
    public IHost Host;
    private readonly ITestOutputHelper logger;

    public App(ITestOutputHelper logger)
    {
        this.logger = logger;
    }

    public App Configure(Action<IServiceCollection>? configure = null)
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