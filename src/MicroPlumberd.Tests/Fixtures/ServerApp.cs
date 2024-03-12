using EventStore.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestPlatform.CommunicationUtilities;
using ModelingEvolution.DirectConnect;
using System.Net.Sockets;
using System.Net;

namespace MicroPlumberd.Tests.Fixtures;

public class PortSearcher
{
    private int lastPort = 2700;
    public int FindNextAvailablePort()
    {
        var actual = Interlocked.Increment(ref this.lastPort);
        while (true)
        {
            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            try
            {
                
                socket.Bind(new IPEndPoint(IPAddress.Loopback, actual));
                socket.Listen();
                socket.Close();
                socket.Dispose();
                Thread.Sleep(100);
                
                return actual;
            }
            catch (SocketException ex)
            {
                // Increment the port number and try again
                actual = Interlocked.Increment(ref this.lastPort);
                socket.Dispose();
            }
        }
    }
}
class ServerApp : IDisposable, IAsyncDisposable
{
    private readonly int _esPort;
    private WebApplication app;

    public ServerApp(int esPort)
    {
        _esPort = esPort;
    }
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
        ((IDisposable)app)?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await app.DisposeAsync();
    }
}