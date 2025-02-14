using EventStore.Client;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
        EventStoreClientSettings? settings = null, Action<IServiceProvider, IPlumberConfig>? configure = null, bool scopedCommandBus = false, int commandBusPoolSize = 64) =>
        collection.AddPlumberd(sp => settings, configure, scopedCommandBus, commandBusPoolSize);

    /// <summary>
    /// Adds the MicroPlumberd services to the specified <see cref="IServiceCollection"/>.
    /// </summary>
    /// <param name="collection">
    /// The <see cref="IServiceCollection"/> to which the services will be added.
    /// </param>
    /// <param name="settingsFactory">
    /// A factory function to create <see cref="EventStoreClientSettings"/> for configuring the Event Store client.
    /// </param>
    /// <param name="configure">
    /// An optional action to configure the <see cref="IPlumberConfig"/> instance.
    /// </param>
    /// <param name="scopedCommandBus">
    /// A boolean value indicating whether the <see cref="ICommandBus"/> should be registered as scoped.
    /// </param>
    /// <param name="commandBusPoolSize">
    /// The size of the command bus pool - it's used for QueueAsync operation on ICommandBus. Defaults to 64.
    /// </param>
    /// <returns>
    /// The updated <see cref="IServiceCollection"/> with the MicroPlumberd services added.
    /// </returns>
    public static IServiceCollection AddPlumberd(this IServiceCollection collection,
        Func<IServiceProvider, EventStoreClientSettings> settingsFactory, Action<IServiceProvider, IPlumberConfig>? configure = null, bool scopedCommandBus = false, int commandBusPoolSize=64)
    {
        collection.AddSingleton(sp => Plumber.Create(settingsFactory(sp), x =>
        {
            configure?.Invoke(sp, x);
            x.ServiceProvider = sp;
        }));
        collection.AddSingleton<StartupHealthCheck>();
        
        collection.AddBackgroundServiceIfMissing<CommandHandlerService>();
        collection.AddBackgroundServiceIfMissing<EventHandlerService>();
        
        collection.TryAddSingleton(typeof(ISnapshotPolicy<>), typeof(AttributeSnaphotPolicy<>));
        if (scopedCommandBus)
        {
            collection.TryAddScoped<ICommandBus>(sp =>
            {
                var pool = (CommandBusPoolScoped)sp.GetRequiredService<ICommandBusPool>();
                var sb = new CommandBus(sp.GetRequiredService<IPlumber>(), pool,
                    sp.GetRequiredService<ILogger<CommandBus>>());
                pool.Init();
                return sb;
            });
            collection.TryAddSingleton<ICommandBusPool>(sp => new CommandBusPoolScoped(sp, commandBusPoolSize));
        }
        else
        {
            collection.TryAddSingleton<ICommandBus, CommandBus>();
            collection.TryAddSingleton<ICommandBusPool>(sp => new CommandBusPool(sp, commandBusPoolSize).Init());
        }
        collection.TryAddSingleton(typeof(IEventHandler<>), typeof(EventHandlerExecutor<>));
        
        
        collection.TryDecorate<ICommandBus, CommandBusAttributeValidator>();

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
        if (serviceDescriptor != null) return services;
        
        services.TryAddSingleton<TService>();
        services.AddHostedService(sp => sp.GetRequiredService<TService>());

        return services;
    }
    public static IServiceCollection AddEventHandler<TEventHandler>(this IServiceCollection services, bool persistently = false, FromStream? start = null) where TEventHandler : class, IEventHandler, ITypeRegister
    {
        services.AddSingleton<EventHandlerStarter<TEventHandler>>();
        services.AddSingleton<IEventHandlerStarter>(sp => sp.GetRequiredService<EventHandlerStarter<TEventHandler>>().Configure(persistently, start));
        services.AddSingleton<IEventHandler<TEventHandler>, ScopedEventHandlerExecutor<TEventHandler>>();
        services.TryAddScoped<TEventHandler>();
        return services;
    }
    public static IServiceCollection AddStateEventHandler<TEventHandler>(this IServiceCollection services) where TEventHandler : class, IEventHandler, ITypeRegister
    {   
        services.AddSingleton<EventStateHandlerStarter<TEventHandler>>();
        services.AddSingleton<IEventHandlerStarter>(sp => sp.GetRequiredService<EventStateHandlerStarter<TEventHandler>>().Configure(FromRelativeStreamPosition.End-1));
        services.AddSingleton<IEventHandler<TEventHandler>, ScopedEventHandlerExecutor<TEventHandler>>();
        services.TryAddScoped<TEventHandler>();
        return services;
    }
    public static IServiceCollection AddEventHandler<TEventHandler>(this IServiceCollection services, FromRelativeStreamPosition start) where TEventHandler : class, IEventHandler, ITypeRegister
    {
        services.AddSingleton<EventHandlerStarter<TEventHandler>>();
        services.AddSingleton<IEventHandlerStarter>(sp => sp.GetRequiredService<EventHandlerStarter<TEventHandler>>().Configure(start));
        services.AddSingleton<IEventHandler<TEventHandler>, ScopedEventHandlerExecutor<TEventHandler>>();
        services.TryAddScoped<TEventHandler>();
        return services;
    }
    public static IServiceCollection AddCommandHandler<TCommandHandler>(this IServiceCollection services, bool persistently = false, StreamPosition? start = null) where TCommandHandler:ICommandHandler, IServiceTypeRegister
    {
        services.AddSingleton<CommandHandlerStarter<TCommandHandler>>();
        services.AddSingleton<ICommandHandlerStarter>(sp => sp.GetRequiredService<CommandHandlerStarter<TCommandHandler>>().Configure(persistently, start));
        services.TryAddSingleton(typeof(CommandHandlerExecutor<>));
        TCommandHandler.RegisterHandlers(services);
        return services;
    }
}