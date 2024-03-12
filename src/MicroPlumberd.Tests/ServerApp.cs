using EventStore.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using ModelingEvolution.DirectConnect;

namespace MicroPlumberd.Tests;

class ServerApp : IDisposable, IAsyncDisposable
{
    private WebApplication app;

    public async Task<IServiceProvider> StartAsync(Action<IServiceCollection> configure = null)
    {
        var builder = WebApplication.CreateBuilder();

        // Add services to the container.
        builder.Services.AddGrpc();

        configure?.Invoke(builder.Services);
        builder.Services.AddSingleton<IPlumber>(sp => sp.GetRequiredService<Plumber>());
        builder.Services.AddSingleton(new Plumber(GetEventStoreSettings()));
        // Adding a decorator for logging.
        builder.Services.TryDecorate(typeof(IRequestHandler<>), typeof(LoggerRequestAspect<>));
        builder.Services.TryDecorate(typeof(IRequestHandler<,>), typeof(LoggerRequestResponseAspect<,>));

        builder.WebHost.ConfigureKestrel(options =>
        {
            // Setup a HTTP/2 endpoint without TLS for development purposes.
            options.ListenLocalhost(5001, o => o.Protocols = HttpProtocols.Http2);
        });

        this.app = builder.Build();

        // Configure the HTTP request pipeline.
        app.MapDirectConnect();
        app.MapGet("/", () => "This server supports gRPC");

        await app.StartAsync();
        return app.Services;
    }
    private static EventStoreClientSettings GetEventStoreSettings()
    {
        const string connectionString = "esdb://admin:changeit@localhost:2113?tls=false&tlsVerifyCert=false";

        return EventStoreClientSettings.Create(connectionString);
    }
    public void Dispose()
    {
        ((IDisposable)app)?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await app.DisposeAsync();
    }
}