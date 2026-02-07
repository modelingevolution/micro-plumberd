using System.Runtime.CompilerServices;
using MicroPlumberd;
using MicroPlumberd.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelingEvolution.EventAggregator;

[assembly: InternalsVisibleTo("MicroPlumberd.Services.EventAggregator.Tests")]

namespace MicroPlumberd.Services.EventAggregator;

/// <summary>
/// Extension methods for registering MicroPlumberd event handlers with EventAggregator as the event source.
/// </summary>
public static class ContainerExtensions
{
    /// <summary>
    /// Registers a scoped event handler that receives events from EventAggregator via <c>EventEnvelope&lt;TId, TEvent&gt;</c>
    /// instead of EventStore subscriptions. The handler uses the same <c>Given(Metadata, TEvent)</c> pattern.
    /// Stream naming follows the <c>StreamNameFromEventConvention</c> so that <c>Metadata.StreamId&lt;TId&gt;()</c>
    /// correctly resolves the recipient identifier.
    /// <para>
    /// A new DI scope is created per event via <see cref="ScopedEventHandlerExecutor{TOwner}"/>,
    /// giving the handler and its dependencies (e.g. DbContext) proper lifetime management.
    /// </para>
    /// </summary>
    /// <typeparam name="THandler">The event handler type. Must implement <see cref="IEventHandler"/> and <see cref="ITypeRegister"/>.</typeparam>
    /// <typeparam name="TId">The type of the recipient identifier. Must implement <see cref="IParsable{TId}"/>.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddScopedEventAggregatorHandler<THandler, TId>(
        this IServiceCollection services)
        where THandler : class, IEventHandler, ITypeRegister
        where TId : IParsable<TId>
    {
        services.TryAddScoped<THandler>();
        services.AddSingleton<EventAggregatorEventHandlerStarter<THandler, TId>>();
        services.AddSingleton<IEventHandlerStarter>(sp =>
            sp.GetRequiredService<EventAggregatorEventHandlerStarter<THandler, TId>>());
        // ScopedEventHandlerExecutor creates a new scope per event so the handler and its
        // scoped dependencies (e.g. DbContext) get proper lifetime management.
        services.AddSingleton<IEventHandler<THandler>, ScopedEventHandlerExecutor<THandler>>();
        return services;
    }

    /// <summary>
    /// Registers a singleton event handler that receives events from EventAggregator via <c>EventEnvelope&lt;TId, TEvent&gt;</c>
    /// instead of EventStore subscriptions. The handler uses the same <c>Given(Metadata, TEvent)</c> pattern.
    /// Stream naming follows the <c>StreamNameFromEventConvention</c> so that <c>Metadata.StreamId&lt;TId&gt;()</c>
    /// correctly resolves the recipient identifier.
    /// <para>
    /// Delegates directly to the singleton handler via <see cref="EventHandlerExecutor{TOwner}"/>
    /// without creating a scope per event — the handler is singleton and resolves to the same instance regardless.
    /// </para>
    /// </summary>
    /// <typeparam name="THandler">The event handler type. Must implement <see cref="IEventHandler"/> and <see cref="ITypeRegister"/>.</typeparam>
    /// <typeparam name="TId">The type of the recipient identifier. Must implement <see cref="IParsable{TId}"/>.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddSingletonEventAggregatorHandler<THandler, TId>(
        this IServiceCollection services)
        where THandler : class, IEventHandler, ITypeRegister
        where TId : IParsable<TId>
    {
        services.AddSingleton<THandler>();
        services.AddSingleton<EventAggregatorEventHandlerStarter<THandler, TId>>();
        services.AddSingleton<IEventHandlerStarter>(sp =>
            sp.GetRequiredService<EventAggregatorEventHandlerStarter<THandler, TId>>());
        // EventHandlerExecutor delegates directly to the handler without creating a scope per event.
        services.AddSingleton<IEventHandler<THandler>>(sp =>
            new EventHandlerExecutor<THandler>(sp.GetRequiredService<THandler>()));
        return services;
    }

    /// <summary>
    /// Registers an event type for fast in-process delivery via EventAggregator as a side-channel
    /// alongside EventStore persistence. When <c>plumber.AppendEvent()</c> is called for this
    /// event type, the event is immediately published on a local <see cref="IEventAggregator"/>
    /// before the EventStore write — delivering it to subscribed <see cref="IEventHandler"/>
    /// instances with minimal latency. EventStore is always written to regardless.
    /// <para>
    /// Use <paramref name="broadcast"/> to also send the event to all Blazor circuits
    /// via <see cref="IEventAggregatorPool"/>.
    /// </para>
    /// </summary>
    /// <typeparam name="TEvent">The event type to propagate.</typeparam>
    /// <typeparam name="TId">The recipient identifier type. Must implement <see cref="IParsable{TId}"/>.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="broadcast">
    /// If false (default), the event is only delivered to EventHandlers inside MicroPlumberd.
    /// If true, the event is also broadcast via <see cref="IEventAggregatorPool"/> to all circuits.
    /// </param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddEventAggregatorPropagation<TEvent, TId>(
        this IServiceCollection services, bool broadcast = false)
        where TId : IParsable<TId>
    {
        var registry = EnsurePropagationRegistry(services);
        registry.Register<TEvent, TId>(broadcast);
        return services;
    }

    private static EventAggregatorPropagationRegistry EnsurePropagationRegistry(IServiceCollection services)
    {
        var existing = services.FirstOrDefault(d => d.ImplementationInstance is EventAggregatorPropagationRegistry);
        if (existing != null)
            return (EventAggregatorPropagationRegistry)existing.ImplementationInstance!;

        var registry = new EventAggregatorPropagationRegistry();
        services.AddSingleton(registry);
        services.TryAddSingleton<EventAggregatorPropagation>(sp =>
            new EventAggregatorPropagation(sp.GetRequiredService<IEventAggregatorPool>()));
        return registry;
    }
}
