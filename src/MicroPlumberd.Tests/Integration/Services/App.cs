using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MicroPlumberd.Tests.Integration.Services;

public class App : IDisposable
{
    public IHost Host;

    public App Configure(Action<IServiceCollection>? configure = null)
    {
        Host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
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