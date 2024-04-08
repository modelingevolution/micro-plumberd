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