namespace MicroPlumberd;

/// <summary>
/// Manages subscription lifecycles for event handlers, providing methods to register and run event handlers with various configurations.
/// </summary>
public interface ISubscriptionRunner : IAsyncDisposable
{
    /// <summary>
    /// Registers an event handler instance that implements both <see cref="IEventHandler"/> and <see cref="ITypeRegister"/>.
    /// </summary>
    /// <typeparam name="T">The type of the event handler, which must implement <see cref="IEventHandler"/> and <see cref="ITypeRegister"/>.</typeparam>
    /// <param name="model">The event handler instance to register.</param>
    /// <returns>A task that represents the asynchronous operation, containing the registered handler.</returns>
    Task<T> WithHandler<T>(T model) where T : IEventHandler, ITypeRegister;

    /// <summary>
    /// Registers an event handler instance with a custom type event converter.
    /// </summary>
    /// <typeparam name="T">The type of the event handler, which must implement <see cref="IEventHandler"/>.</typeparam>
    /// <param name="model">The event handler instance to register.</param>
    /// <param name="mapFunc">A function to convert event type names to types.</param>
    /// <returns>A task that represents the asynchronous operation, containing the registered handler.</returns>
    Task<T> WithHandler<T>(T model, TypeEventConverter mapFunc) where T : IEventHandler;

    /// <summary>
    /// Creates and registers an event handler of the specified type.
    /// </summary>
    /// <typeparam name="T">The type of the event handler, which must implement <see cref="IEventHandler"/> and <see cref="ITypeRegister"/>.</typeparam>
    /// <returns>A task that represents the asynchronous operation, containing the created and registered handler.</returns>
    Task<IEventHandler> WithHandler<T>() where T : IEventHandler, ITypeRegister;

    /// <summary>
    /// Creates and registers an event handler of the specified type with a custom type event converter.
    /// </summary>
    /// <typeparam name="T">The type of the event handler, which must implement <see cref="IEventHandler"/>.</typeparam>
    /// <param name="mapFunc">A function to convert event type names to types.</param>
    /// <returns>A task that represents the asynchronous operation, containing the created and registered handler.</returns>
    Task<IEventHandler> WithHandler<T>(TypeEventConverter mapFunc) where T : IEventHandler;

    /// <summary>
    /// Creates and registers an event handler of the specified type with a custom type handler register.
    /// </summary>
    /// <typeparam name="T">The type of the event handler, which must implement <see cref="IEventHandler"/> and <see cref="ITypeRegister"/>.</typeparam>
    /// <param name="register">The type handler register to use for event type resolution.</param>
    /// <returns>A task that represents the asynchronous operation, containing the created and registered handler.</returns>
    Task<IEventHandler> WithHandler<T>(ITypeHandlerRegisters register) where T : IEventHandler, ITypeRegister;

    /// <summary>
    /// Creates and registers a snapshot-based event handler of the specified type.
    /// </summary>
    /// <typeparam name="T">The type of the event handler, which must implement <see cref="IEventHandler"/> and <see cref="ITypeRegister"/>.</typeparam>
    /// <returns>A task that represents the asynchronous operation, containing the created and registered snapshot handler.</returns>
    Task<IEventHandler> WithSnapshotHandler<T>() where T : IEventHandler, ITypeRegister;

    /// <summary>
    /// Registers a snapshot-based event handler instance.
    /// </summary>
    /// <typeparam name="T">The type of the event handler, which must implement <see cref="IEventHandler"/> and <see cref="ITypeRegister"/>.</typeparam>
    /// <param name="model">The event handler instance to register.</param>
    /// <returns>A task that represents the asynchronous operation, containing the registered snapshot handler.</returns>
    Task<IEventHandler> WithSnapshotHandler<T>(T model) where T : IEventHandler, ITypeRegister;

    /// <summary>
    /// Registers an event handler instance with a custom type handler register.
    /// </summary>
    /// <typeparam name="T">The type of the event handler, which must implement <see cref="IEventHandler"/> and <see cref="ITypeRegister"/>.</typeparam>
    /// <param name="model">The event handler instance to register.</param>
    /// <param name="register">The type handler register to use for event type resolution.</param>
    /// <returns>A task that represents the asynchronous operation, containing the registered handler.</returns>
    Task<IEventHandler> WithHandler<T>(T model, ITypeHandlerRegisters register) where T : IEventHandler, ITypeRegister;
}