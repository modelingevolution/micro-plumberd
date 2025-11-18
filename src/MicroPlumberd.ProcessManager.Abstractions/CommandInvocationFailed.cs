namespace MicroPlumberd;

/// <summary>
/// Represents an event indicating that a command invocation has failed.
/// </summary>
public record CommandInvocationFailed
{
    /// <summary>
    /// Gets or initializes the unique identifier of the command recipient aggregate.
    /// </summary>
    public Guid RecipientId { get; init; }

    /// <summary>
    /// Gets or initializes the command request that failed to execute.
    /// </summary>
    public ICommandRequest Command { get; init; }

    /// <summary>
    /// Gets or initializes the error message describing why the command invocation failed.
    /// </summary>
    public string Message { get; init; }
}