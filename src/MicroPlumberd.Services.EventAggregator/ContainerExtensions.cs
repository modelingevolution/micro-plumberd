using System.Runtime.CompilerServices;
using MicroPlumberd;
using MicroPlumberd.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

[assembly: InternalsVisibleTo("MicroPlumberd.Services.EventAggregator.Tests")]

namespace MicroPlumberd.Services.EventAggregator;

/// <summary>
/// Extension methods for registering MicroPlumberd event handlers with EventAggregator as the event source.
/// </summary>
public static class ContainerExtensions
{
    /// <summary>
    /// Registers an event handler that receives events from EventAggregator via <c>EventEnvelope&lt;TId, TEvent&gt;</c>
    /// instead of EventStore subscriptions. The handler uses the same <c>Given(Metadata, TEvent)</c> pattern.
    /// Stream naming follows the <c>StreamNameFromEventConvention</c> so that <c>Metadata.StreamId&lt;TId&gt;()</c>
    /// correctly resolves the recipient identifier.
    /// </summary>
    /// <typeparam name="THandler">The event handler type. Must implement <see cref="IEventHandler"/> and <see cref="ITypeRegister"/>.</typeparam>
    /// <typeparam name="TId">The type of the recipient identifier. Must implement <see cref="IParsable{TId}"/>.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddEventHandlerWithEventAggregatorSource<THandler, TId>(
        this IServiceCollection services)
        where THandler : class, IEventHandler, ITypeRegister
        where TId : IParsable<TId>
    {
        services.TryAddScoped<THandler>();
        services.AddSingleton<EventAggregatorEventHandlerStarter<THandler, TId>>();
        services.AddSingleton<IEventHandlerStarter>(sp =>
            sp.GetRequiredService<EventAggregatorEventHandlerStarter<THandler, TId>>());
        services.AddSingleton<IEventHandler<THandler>, ScopedEventHandlerExecutor<THandler>>();
        return services;
    }
}
