using EventStore.Client;
using MicroPlumberd.DirectConnect;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace MicroPlumberd.Services;

class EventHandlerExecutor<TOwner>(TOwner handler) : IEventHandler<TOwner>
    where TOwner : IEventHandler
{
    public Task Handle(Metadata m, object ev) => handler.Handle(m, ev);
}
class ScopedEventHandlerExecutor<TOwner>(IServiceProvider sp) : IEventHandler<TOwner>
    where TOwner : IEventHandler
{
    public async Task Handle(Metadata m, object ev)
    {
        await using var scope = sp.CreateAsyncScope(); 
        await scope.ServiceProvider.GetRequiredService<TOwner>().Handle(m, ev);
    }
}
public static class ContainerExtensions
{
    public static IServiceCollection AddPlumberd(this IServiceCollection collection,
        EventStoreClientSettings? settings = null, Action<IPlumberConfig>? configure = null)
    {
        collection.AddSingleton(sp => Plumber.Create(settings, x =>
        {
            configure?.Invoke(x);
            x.ServiceProvider = sp;
        }));
        collection.TryAddSingleton<ICommandBus, CommandBus>();
        collection.TryAddSingleton( typeof(IEventHandler<>),typeof(EventHandlerExecutor<>));
        return collection;
    }

    public static IServiceCollection AddBackgroundServiceIfMissing<TService>(this IServiceCollection services)
        where TService : BackgroundService
    {
        // Check if the service is already added
        var serviceDescriptor = services.FirstOrDefault(descriptor =>
            descriptor.ServiceType == typeof(IHostedService) &&
            descriptor.ImplementationType == typeof(TService));

        // Add the service if it's missing
        if (serviceDescriptor == null)
        {
            services.AddHostedService<TService>();
        }

        return services;
    }
    public static IServiceCollection AddEventHandler<TEventHandler>(this IServiceCollection services, bool persistently = false, FromStream? start = null) where TEventHandler : class, IEventHandler, ITypeRegister
    {
        services.AddBackgroundServiceIfMissing<EventHandlerService>();
        services.AddSingleton<EventHandlerStarter<TEventHandler>>();
        services.AddSingleton<IEventHandlerStarter>(sp => sp.GetRequiredService<EventHandlerStarter<TEventHandler>>().Configure(persistently, start));
        services.AddSingleton<IEventHandler<TEventHandler>, ScopedEventHandlerExecutor<TEventHandler>>();
        services.TryAddScoped<TEventHandler>();
        return services;
    }
    public static IServiceCollection AddCommandHandler<TCommandHandler>(this IServiceCollection services) where TCommandHandler:ICommandHandler, IServiceTypeRegister
    {
        services.AddBackgroundServiceIfMissing<CommandHandlerService>();
        services.AddSingleton<ICommandHandlerStarter, CommandHandlerStarter<TCommandHandler>>();
        services.TryAddSingleton(typeof(CommandHandlerExecutor<>));
        TCommandHandler.RegisterHandlers(services);
        return services;
    }
}