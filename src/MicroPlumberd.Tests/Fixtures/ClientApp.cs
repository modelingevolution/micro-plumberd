using Microsoft.Extensions.DependencyInjection;

namespace MicroPlumberd.Tests.Fixtures;

class ClientApp : IDisposable, IAsyncDisposable
{
    private ServiceProvider sp;

    public IServiceProvider Start(Action<IServiceCollection> configure = null)
    {
        IServiceCollection service = new ServiceCollection();


        configure?.Invoke(service);

        return sp = service.BuildServiceProvider();
    }

    public void Dispose()
    {
        sp?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await sp.DisposeAsync();
    }
}