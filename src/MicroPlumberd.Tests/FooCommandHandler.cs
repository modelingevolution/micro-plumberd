using EventStore.Client;
using MicroPlumberd.DirectConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using ModelingEvolution.DirectConnect;
using ProtoBuf;

namespace MicroPlumberd.Tests;

[ProtoContract]
public class CreateFoo : ICommand
{
    [ProtoMember(2)]
    public string Name { get; set; }
    [ProtoMember(1)]
    public Guid Id { get; set; } = Guid.NewGuid();
}
[ProtoContract]
public class ChangeFoo : ICommand
{
    [ProtoMember(2)]
    public string Name { get; set; }
    [ProtoMember(1)]
    public Guid Id { get; set; } = Guid.NewGuid();
}
[ProtoContract]
public class Results
{
    [ProtoMember(1)]
    public int Code { get; init; }
    public static Results Ok() => new Results() { Code = 200 };
    public static FaultException<TFault> Fault<TFault>(TFault fault) => new FaultException<TFault>(fault);
}
[ProtoContract]
public class BusinessFault { }

[CommandHandler]
public partial class FooCommandHandler(IPlumber plumber)
{
    
    [ThrowsFaultException<BusinessFault>]
    public async Task<Results> Handle(Guid id, CreateFoo cmd)
    {
        if (cmd.Name == "error") 
            throw Results.Fault(new BusinessFault());

        var agg = FooAggregate.New(id);
        agg.Open(cmd.Name);

        await plumber.SaveNew(agg);
        return Results.Ok();
    }

    [ThrowsFaultException<BusinessFault>]
    public async Task<Results> Handle(Guid id, ChangeFoo cmd)
    {
        if (cmd.Name == "error")
            throw Results.Fault(new BusinessFault());

        var agg = await plumber.Get<FooAggregate>(id);
        agg.Change(cmd.Name);

        await plumber.SaveChanges(agg);
        return Results.Ok();
    }
}



class ClientApp : IDisposable, IAsyncDisposable
{
    private ServiceProvider sp;

    public IServiceProvider Start(Action<IServiceCollection> configure = null)
    {
        IServiceCollection service = new ServiceCollection();


        configure?.Invoke(service);

        return this.sp = service.BuildServiceProvider();
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