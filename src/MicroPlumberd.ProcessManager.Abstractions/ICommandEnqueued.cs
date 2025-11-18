namespace MicroPlumberd;

/// <summary>
/// Interface representing a command that has been enqueued for execution by a process manager.
/// </summary>
public interface ICommandEnqueued
{
    /// <summary>
    /// Gets the command object that has been enqueued.
    /// </summary>
    object Command { get; }

    /// <summary>
    /// Gets the unique identifier of the command recipient aggregate.
    /// </summary>
    Guid RecipientId { get; }
}