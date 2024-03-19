using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MicroPlumberd.Tests.Integration.Services;

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