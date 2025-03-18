using EventStore.Client;
using MicroPlumberd.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestPlatform.CommunicationUtilities;
using ModelingEvolution.DirectConnect;

namespace MicroPlumberd.Tests.Integration.Services.Grpc.DirectConnect.Fixtures;

class ServerApp : IDisposable, IAsyncDisposable
{
    private static PortSearcher _portSearcher = new PortSearcher(5001);
    private readonly int _esPort;
    private WebApplication? app;
    public int HttpPort { get; private set; }
    public ServerApp(int esPort)
    {
        _esPort = esPort;
        HttpPort = _portSearcher.FindNextAvailablePort();
    }
    public async Task<IServiceProvider> StartAsync(Action<IServiceCollection>? configure = null)
    {
        var builder = WebApplication.CreateBuilder();

        // Add services to the container.
        builder.Services.AddGrpc();

        configure?.Invoke(builder.Services);
        builder.Services.AddSingleton<IPlumber>(sp => sp.GetRequiredService<Plumber>());
        builder.Services.AddSingleton(new PlumberEngine(GetEventStoreSettings()));
        builder.Services.AddScoped<IPlumber, Plumber>();
        builder.Services.AddScoped<OperationContext>(sp => new OperationContext(Flow.Component));

        // Adding a decorator for logging.
        builder.Services.TryDecorate(typeof(IRequestHandler<>), typeof(LoggerRequestAspect<>));
        builder.Services.TryDecorate(typeof(IRequestHandler<,>), typeof(LoggerRequestResponseAspect<,>));

        builder.WebHost.ConfigureKestrel(options =>
        {
            // Setup a HTTP/2 endpoint without TLS for development purposes.
            options.ListenLocalhost(HttpPort, o => o.Protocols = HttpProtocols.Http2);
        });

        app = builder.Build();

        // Configure the HTTP request pipeline.
        app.MapDirectConnect();
        app.MapGet("/", () => "This server supports gRPC");

        await app.StartAsync();
        return app.Services;
    }
    private EventStoreClientSettings GetEventStoreSettings()
    {
        string connectionString = $"esdb://admin:changeit@localhost:{_esPort}?tls=false&tlsVerifyCert=false";

        return EventStoreClientSettings.Create(connectionString);
    }
    public void Dispose()
    {
        (app as IDisposable)?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (app != null)
            await app.DisposeAsync();
    }
}