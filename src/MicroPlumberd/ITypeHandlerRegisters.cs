namespace MicroPlumberd;

/// <summary>
/// Manages type mappings and event name conversions for event handlers.
/// Provides registration and retrieval of event type information for different handler types.
/// </summary>
public interface ITypeHandlerRegisters
{
    /// <summary>
    /// Gets the collection of all registered handler types.
    /// </summary>
    /// <value>An enumerable collection of handler types that have been registered.</value>
    IEnumerable<Type> HandlerTypes { get; }

    /// <summary>
    /// Gets a type event converter function for the specified handler type.
    /// </summary>
    /// <typeparam name="THandler">The handler type, which must implement <see cref="ITypeRegister"/>.</typeparam>
    /// <returns>A <see cref="TypeEventConverter"/> function that can convert event type names to <see cref="Type"/> instances.</returns>
    TypeEventConverter GetEventNameConverterFor<THandler>() where THandler:ITypeRegister;

    /// <summary>
    /// Gets the event name to type mappings for the specified handler.
    /// </summary>
    /// <typeparam name="THandler">The handler type, which must implement <see cref="ITypeRegister"/>.</typeparam>
    /// <returns>An enumerable collection of key-value pairs mapping event names to their corresponding types.</returns>
    IEnumerable<KeyValuePair<string, Type>> GetEventNameMappingsFor<THandler>() where THandler : ITypeRegister;

    /// <summary>
    /// Gets all event names that the specified handler can process.
    /// </summary>
    /// <typeparam name="THandler">The handler type, which must implement <see cref="ITypeRegister"/>.</typeparam>
    /// <returns>An enumerable collection of event names that the handler supports.</returns>
    IEnumerable<string> GetEventNamesFor<THandler>() where THandler : ITypeRegister;
}


