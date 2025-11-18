using EventStore.Client;

namespace MicroPlumberd;


/// <summary>
/// Interface for EventHandlers.
/// </summary>
/// <typeparam name="TOwner">The type of the owner.</typeparam>
/// <seealso cref="MicroPlumberd.IEventHandler" />
public interface IEventHandler<TOwner> : IEventHandler 
where TOwner:IEventHandler{ }

/// <summary>
/// Dispatching interface for EventHandlers.
/// </summary>
/// <seealso cref="MicroPlumberd.IEventHandler" />
public interface IEventHandler
{
    /// <summary>
    /// Dispatching method. Handles the specified metadata and event.
    /// </summary>
    /// <param name="m">The m.</param>
    /// <param name="ev">The ev.</param>
    /// <returns></returns>
    Task Handle(Metadata m, object ev);
}

/// <summary>
/// Interface for event-handlers to be invoke when a subscription has catchup.
/// </summary>
public interface ICaughtUpHandler
{
    /// <summary>
    /// Called when the subscription has caught up with all historical events and is now processing live events.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task CaughtUp();
}

/// <summary>
/// Subscription set builder.
/// </summary>
public interface ISubscriptionSet
{
    /// <summary>
    /// Withes the specified model.
    /// </summary>
    /// <typeparam name="TModel">The type of the model.</typeparam>
    /// <param name="model">The model.</param>
    /// <returns></returns>
    ISubscriptionSet With<TModel>(TModel model)
        where TModel : IEventHandler, ITypeRegister;

    /// <summary>
    /// Subscribes persistently.
    /// </summary>
    /// <param name="outputStream">The output stream.</param>
    /// <param name="groupName">Name of the group.</param>
    /// <returns></returns>
    Task SubscribePersistentlyAsync(string outputStream, string? groupName = null);

    /// <summary>
    /// Subscribes to stream.
    /// </summary>
    /// <param name="name">The name of the stream.</param>
    /// <param name="start">The start.</param>
    /// <returns></returns>
    Task SubscribeAsync(string name, FromStream start);
}

/// <summary>
/// Engine-level subscription set builder interface for internal use.
/// </summary>
public interface IEngineSubscriptionSet
{
    /// <summary>
    /// Adds an event handler model to the subscription set.
    /// </summary>
    /// <typeparam name="TModel">The type of the model.</typeparam>
    /// <param name="model">The model.</param>
    /// <returns>The subscription set for fluent chaining.</returns>
    IEngineSubscriptionSet With<TModel>(TModel model)
        where TModel : IEventHandler, ITypeRegister;

    /// <summary>
    /// Subscribes all models in the set persistently to the specified output stream.
    /// </summary>
    /// <param name="context">The operation context.</param>
    /// <param name="outputStream">The output stream.</param>
    /// <param name="groupName">Name of the group.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SubscribePersistentlyAsync(OperationContext context, string outputStream, string? groupName = null);

    /// <summary>
    /// Subscribes all models in the set to the specified stream.
    /// </summary>
    /// <param name="context">The operation context.</param>
    /// <param name="name">The name of the stream.</param>
    /// <param name="start">The start position.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SubscribeAsync(OperationContext context, string name, FromStream start);
}