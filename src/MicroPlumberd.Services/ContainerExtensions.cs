using EventStore.Client;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MicroPlumberd.Services;

/// <summary>
/// Executes event handlers using a singleton handler instance.
/// </summary>
/// <typeparam name="TOwner">The type of event handler.</typeparam>
class EventHandlerExecutor<TOwner>(TOwner handler) : IEventHandler<TOwner>
    where TOwner : IEventHandler
{
    /// <inheritdoc/>
    public Task Handle(Metadata m, object ev) => handler.Handle(m, ev);
}
/// <summary>
/// Executes event handlers within a scoped service provider.
/// </summary>
/// <typeparam name="TOwner">The type of event handler.</typeparam>
class ScopedEventHandlerExecutor<TOwner>(IServiceProvider sp) : IEventHandler<TOwner>
    where TOwner : IEventHandler
{
    /// <inheritdoc/>
    public async Task Handle(Metadata m, object ev)
    {
        await using var scope = sp.CreateAsyncScope(); 
        
        await scope.ServiceProvider.GetRequiredService<TOwner>().Handle(m, ev);
    }
}
/// <summary>
/// Provides extension methods for registering MicroPlumberd services with the dependency injection container.
/// </summary>
public static class ContainerExtensions
{
    /// <summary>
    /// Adds MicroPlumberd services to the specified service collection.
    /// </summary>
    /// <param name="collection">The service collection to add services to.</param>
    /// <param name="settings">The EventStore client settings. If null, default settings will be used.</param>
    /// <param name="configure">An optional action to configure the plumber configuration.</param>
    /// <param name="scopedCommandBus">If true, registers the command bus as scoped; otherwise, as singleton.</param>
    /// <param name="commandBusPoolSize">The size of the command bus pool for QueueAsync operations. Defaults to 64.</param>
    /// <returns>The service collection for method chaining.</returns>
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
        collection.AddSingleton(sp => PlumberEngine.Create(settingsFactory(sp), x =>
        {
            configure?.Invoke(sp, x);
            x.ServiceProvider = sp;
        }));
        collection.AddScoped<IPlumber, Plumber>();
        collection.AddSingleton<IPlumberInstance, PlumberInstance>();
        collection.AddScoped<OperationContext>(sp => OperationContext.Create(Flow.Component));

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
            collection.TryAddSingleton<ICommandBus>(sp =>
            {
                var pool = sp.GetRequiredService<ICommandBusPool>();
                var sb = new CommandBus(sp.GetRequiredService<IPlumberInstance>(), pool,
                    sp.GetRequiredService<ILogger<CommandBus>>());
                return sb;
            });
            collection.TryAddSingleton<ICommandBusPool>(sp => new CommandBusPool(sp, commandBusPoolSize).Init());
        }
        collection.TryAddSingleton(typeof(IEventHandler<>), typeof(EventHandlerExecutor<>));

        // Decorator chain (outermost first):
        // CommandBusAttributeValidator → InProcCommandBusDecorator → CommandBus
        // InProc is registered first so it wraps CommandBus directly.
        // The validator is registered second so it wraps InProc.
        // InProcCommandBusDecorator is a no-op if no command types are registered in InProcCommandRegistry.
        // Empty registry ensures the decorator resolves even if AddCommandInProcExecutor was never called.
        collection.TryAddSingleton(new InProcCommandRegistry());
        collection.TryDecorate<ICommandBus, InProcCommandBusDecorator>();
        collection.TryDecorate<ICommandBus, CommandBusAttributeValidator>();

        return collection;
    }

    /// <summary>
    /// Adds a background service to the service collection if it hasn't been added already.
    /// </summary>
    /// <typeparam name="TService">The type of background service to add.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for method chaining.</returns>
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

    /// <summary>
    /// Adds a scoped event handler to the service collection.
    /// </summary>
    /// <typeparam name="TEventHandler">The type of event handler to add.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="persistently">If true, uses persistent subscriptions; otherwise, uses catch-up subscriptions.</param>
    /// <param name="start">The stream position to start reading from. If null, defaults to the start of the stream.</param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddScopedEventHandler<TEventHandler>(this IServiceCollection services,
        bool persistently = false, FromStream? start = null) where TEventHandler : class, IEventHandler, ITypeRegister
    {
        return services.AddScoped<TEventHandler>().AddEventHandler<TEventHandler>(persistently, start);
    }

    /// <summary>
    /// Adds a singleton event handler to the service collection.
    /// Uses <see cref="EventHandlerExecutor{TOwner}"/> (no scope per event) since the handler is a singleton
    /// and does not need scoped lifetime management. This avoids unnecessary scope creation per event.
    /// </summary>
    /// <typeparam name="TEventHandler">The type of event handler to add.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="persistently">If true, uses persistent subscriptions; otherwise, uses catch-up subscriptions.</param>
    /// <param name="start">The stream position to start reading from. If null, defaults to the start of the stream.</param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddSingletonEventHandler<TEventHandler>(this IServiceCollection services,
        bool persistently = false, FromStream? start = null) where TEventHandler : class, IEventHandler, ITypeRegister
    {
        services.AddSingleton<TEventHandler>();
        services.AddSingleton<EventHandlerStarter<TEventHandler>>();
        services.AddSingleton<IEventHandlerStarter>(sp => sp.GetRequiredService<EventHandlerStarter<TEventHandler>>().Configure(persistently, start));
        // EventHandlerExecutor delegates directly to the handler without creating a scope per event.
        // Previously this used ScopedEventHandlerExecutor which created and disposed a scope per event
        // for no benefit — the handler is singleton and resolves to the same instance regardless.
        services.AddSingleton<IEventHandler<TEventHandler>>(sp =>
            new EventHandlerExecutor<TEventHandler>(sp.GetRequiredService<TEventHandler>()));
        return services;
    }

    /// <summary>
    /// Adds an event handler to the service collection with automatic subscription configuration.
    /// </summary>
    /// <typeparam name="TEventHandler">The type of event handler to add.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="persistently">If true, uses persistent subscriptions; otherwise, uses catch-up subscriptions.</param>
    /// <param name="start">The stream position to start reading from. If null, defaults to the start of the stream.</param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddEventHandler<TEventHandler>(this IServiceCollection services, bool persistently = false, FromStream? start = null) where TEventHandler : class, IEventHandler, ITypeRegister
    {
        services.AddSingleton<EventHandlerStarter<TEventHandler>>();
        services.AddSingleton<IEventHandlerStarter>(sp => sp.GetRequiredService<EventHandlerStarter<TEventHandler>>().Configure(persistently, start));
        // ScopedEventHandlerExecutor creates a new scope per event so the handler and its
        // scoped dependencies (e.g. DbContext) get proper lifetime management.
        services.AddSingleton<IEventHandler<TEventHandler>, ScopedEventHandlerExecutor<TEventHandler>>();
        services.TryAddScoped<TEventHandler>();
        return services;
    }

    /// <summary>
    /// Adds a state event handler that reads from the end of the stream minus one event.
    /// </summary>
    /// <typeparam name="TEventHandler">The type of event handler to add.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddStateEventHandler<TEventHandler>(this IServiceCollection services) where TEventHandler : class, IEventHandler, ITypeRegister
    {
        services.AddSingleton<EventStateHandlerStarter<TEventHandler>>();
        services.AddSingleton<IEventHandlerStarter>(sp => sp.GetRequiredService<EventStateHandlerStarter<TEventHandler>>().Configure(FromRelativeStreamPosition.End-1));
        services.AddSingleton<IEventHandler<TEventHandler>, ScopedEventHandlerExecutor<TEventHandler>>();
        services.TryAddScoped<TEventHandler>();
        return services;
    }

    /// <summary>
    /// Adds an event handler to the service collection with a specific relative stream position.
    /// </summary>
    /// <typeparam name="TEventHandler">The type of event handler to add.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="start">The relative stream position to start reading from.</param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddEventHandler<TEventHandler>(this IServiceCollection services, FromRelativeStreamPosition start) where TEventHandler : class, IEventHandler, ITypeRegister
    {
        services.AddSingleton<EventHandlerStarter<TEventHandler>>();
        services.AddSingleton<IEventHandlerStarter>(sp => sp.GetRequiredService<EventHandlerStarter<TEventHandler>>().Configure(start));
        services.AddSingleton<IEventHandler<TEventHandler>, ScopedEventHandlerExecutor<TEventHandler>>();
        services.TryAddScoped<TEventHandler>();
        return services;
    }

    /// <summary>
    /// Adds a scoped event handler to the service collection with a specific relative stream position.
    /// </summary>
    /// <typeparam name="TEventHandler">The type of event handler to add.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="start">The relative stream position to start reading from.</param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddScopedEventHandler<TEventHandler>(this IServiceCollection services,
        FromRelativeStreamPosition start) where TEventHandler : class, IEventHandler, ITypeRegister
    {
        return services.AddScoped<TEventHandler>().AddEventHandler<TEventHandler>(start);
    }

    /// <summary>
    /// Adds a singleton event handler to the service collection with a specific relative stream position.
    /// </summary>
    /// <typeparam name="TEventHandler">The type of event handler to add.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="start">The relative stream position to start reading from.</param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddSingletonEventHandler<TEventHandler>(this IServiceCollection services,
        FromRelativeStreamPosition start) where TEventHandler : class, IEventHandler, ITypeRegister
    {
        return services.AddSingleton<TEventHandler>().AddEventHandler<TEventHandler>(start);
    }

    /// <summary>
    /// Adds a scoped command handler to the service collection.
    /// </summary>
    /// <typeparam name="TCommandHandler">The type of command handler to add.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="persistently">If true, uses persistent subscriptions; otherwise, uses catch-up subscriptions.</param>
    /// <param name="start">The stream position to start reading from. If null, defaults to the end of the stream.</param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddScopedCommandHandler<TCommandHandler>(this IServiceCollection services,
        bool persistently = false, StreamPosition? start = null)
        where TCommandHandler : class, ICommandHandler, IServiceTypeRegister
    {
        return services
            .AddScoped<TCommandHandler>()
            .AddCommandHandler<TCommandHandler>(persistently, start);
    }

    /// <summary>
    /// Adds a singleton command handler to the service collection.
    /// </summary>
    /// <typeparam name="TCommandHandler">The type of command handler to add.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="persistently">If true, uses persistent subscriptions; otherwise, uses catch-up subscriptions.</param>
    /// <param name="start">The stream position to start reading from. If null, defaults to the end of the stream.</param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddSingletonCommandHandler<TCommandHandler>(this IServiceCollection services,
        bool persistently = false, StreamPosition? start = null)
        where TCommandHandler : class, ICommandHandler, IServiceTypeRegister
    {
        return services
            .AddSingleton<TCommandHandler>()
            .AddCommandHandler<TCommandHandler>(persistently, start,false);
    }

    /// <summary>
    /// Adds a command handler to the service collection with automatic subscription configuration.
    /// </summary>
    /// <typeparam name="TCommandHandler">The type of command handler to add.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="persistently">If true, uses persistent subscriptions; otherwise, uses catch-up subscriptions.</param>
    /// <param name="start">The stream position to start reading from. If null, defaults to the end of the stream.</param>
    /// <param name="scopedExecutor">If true, creates a scoped executor; otherwise, uses a singleton executor.</param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddCommandHandler<TCommandHandler>(this IServiceCollection services,
        bool persistently = false, StreamPosition? start = null, bool scopedExecutor = true) where TCommandHandler:ICommandHandler, IServiceTypeRegister
    {
        Type t = typeof(TCommandHandler);
        services.AddSingleton<CommandHandlerStarter<TCommandHandler>>();
        services.AddSingleton<ICommandHandlerStarter>(sp => sp.GetRequiredService<CommandHandlerStarter<TCommandHandler>>().Configure(persistently, start, scopedExecutor));

        services.AddSingleton(typeof(EventHandlerRootExecutor<>).MakeGenericType(t));

        services.TryAddSingleton(typeof(ICommandHandleExecutor<>).MakeGenericType(t),
            scopedExecutor
            ? typeof(CommandHandlerScopedExecutor<>).MakeGenericType(t)
            : typeof(CommandHandlerSingletonExecutor<>).MakeGenericType(t));

        TCommandHandler.RegisterHandlers(services, scopedExecutor);
        return services;
    }

    /// <summary>
    /// Registers a specific command type for in-process execution.
    /// When the <see cref="ICommandBus"/> receives this command type, it will resolve the handler from DI
    /// and call <see cref="ICommandHandler.Execute"/> directly — skipping EventStore entirely.
    /// The in-process decorator is automatically registered on first use.
    /// </summary>
    /// <typeparam name="TCommand">The command type to execute in-process.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddCommandInProcExecutor<TCommand>(this IServiceCollection services)
    {
        var registry = EnsureInProcDecorator(services);
        var handlerType = typeof(ICommandHandler<TCommand>);
        var isSingleton = services.Any(d => d.ServiceType == handlerType && d.Lifetime == ServiceLifetime.Singleton);
        registry.Register(typeof(TCommand), isSingleton);
        return services;
    }

    /// <summary>
    /// Registers all command types from a command handler for in-process execution.
    /// Uses <see cref="IServiceTypeRegister.CommandTypes"/> to discover all supported commands.
    /// When the <see cref="ICommandBus"/> receives any of these command types, it will resolve the handler from DI
    /// and call <see cref="ICommandHandler.Execute"/> directly — skipping EventStore entirely.
    /// The in-process decorator is automatically registered on first use.
    /// </summary>
    /// <typeparam name="TCommandHandler">The command handler type whose commands should be executed in-process.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddCommandInProcExecutorFor<TCommandHandler>(this IServiceCollection services)
        where TCommandHandler : ICommandHandler, IServiceTypeRegister
    {
        var registry = EnsureInProcDecorator(services);
        foreach (var cmdType in TCommandHandler.CommandTypes)
        {
            var handlerType = typeof(ICommandHandler<>).MakeGenericType(cmdType);
            var isSingleton = services.Any(d => d.ServiceType == handlerType && d.Lifetime == ServiceLifetime.Singleton);
            registry.Register(cmdType, isSingleton);
        }
        return services;
    }

    private static InProcCommandRegistry EnsureInProcDecorator(IServiceCollection services)
    {
        var existing = services.FirstOrDefault(d => d.ImplementationInstance is InProcCommandRegistry);
        if (existing != null)
            return (InProcCommandRegistry)existing.ImplementationInstance!;

        // Registry is singleton instance — decorator is already registered in AddPlumberd().
        var registry = new InProcCommandRegistry();
        services.AddSingleton(registry);
        return registry;
    }
}