// ReSharper disable once CheckNamespace
namespace MicroPlumberd;

/// <summary>
/// Marker interface representing an action that can be performed by a process manager.
/// </summary>
public interface IProcessAction
{

}

/// <summary>
/// Represents a state change action that contains events to be applied to an aggregate owner.
/// </summary>
/// <typeparam name="TOwner">The type of the aggregate that owns this state change.</typeparam>
/// <param name="Id">The unique identifier of the aggregate instance.</param>
/// <param name="Version">The expected version of the aggregate for optimistic concurrency control.</param>
/// <param name="Events">The array of events to be applied to the aggregate.</param>
public record StateChangeAction<TOwner>(Guid Id, long Version, params object[] Events) : IStateChangeAction
{
    /// <summary>
    /// Gets the type of the aggregate owner.
    /// </summary>
    public Type Owner => typeof(TOwner);
}

/// <summary>
/// Non-generic interface representing a state change action in a process manager.
/// </summary>
public interface IStateChangeAction : IProcessAction
{
    /// <summary>
    /// Gets the unique identifier of the aggregate instance.
    /// </summary>
    Guid Id { get;  }

    /// <summary>
    /// Gets the expected version of the aggregate for optimistic concurrency control.
    /// </summary>
    long Version { get; }

    /// <summary>
    /// Gets the array of events to be applied to the aggregate.
    /// </summary>
    object[] Events { get; }

    /// <summary>
    /// Gets the type of the aggregate that owns this state change.
    /// </summary>
    Type Owner { get; }
}

/// <summary>
/// Non-generic interface representing a command request to be sent to a recipient aggregate.
/// </summary>
public interface ICommandRequest : IProcessAction
{
    /// <summary>
    /// Gets the unique identifier of the command recipient aggregate.
    /// </summary>
    Guid RecipientId { get; }

    /// <summary>
    /// Gets the command object to be sent.
    /// </summary>
    object Command { get; }
}

/// <summary>
/// Generic interface representing a strongly-typed command request to be sent to a recipient aggregate.
/// </summary>
/// <typeparam name="TCommand">The type of the command being sent.</typeparam>
public interface ICommandRequest<out TCommand> : ICommandRequest
{
    /// <summary>
    /// Gets the strongly-typed command to be sent.
    /// </summary>
    new TCommand Command { get; }
}