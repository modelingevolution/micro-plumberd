using Microsoft.Extensions.DependencyInjection;

namespace MicroPlumberd.Tests.Integration.Services.Grpc.DirectConnect.Fixtures;

class ClientApp : IDisposable, IAsyncDisposable
{
    private ServiceProvider? sp;

    public IServiceProvider Start(Action<IServiceCollection>? configure = null)
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
        if (sp != null)
            await sp.DisposeAsync();
    }
}