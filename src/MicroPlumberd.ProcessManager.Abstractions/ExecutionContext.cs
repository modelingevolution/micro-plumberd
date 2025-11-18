namespace MicroPlumberd;

/// <summary>
/// Represents the execution context for a process manager operation, including metadata, event information, and any errors that occurred.
/// </summary>
/// <param name="Metadata">The metadata associated with the event or operation.</param>
/// <param name="Event">The event that triggered the process manager execution.</param>
/// <param name="Id">The unique identifier of the process manager instance.</param>
/// <param name="Command">The command request that was being processed, if applicable.</param>
/// <param name="Exception">The exception that occurred during execution, if any.</param>
public record ExecutionContext(Metadata Metadata, object Event, Guid Id, ICommandRequest? Command, Exception Exception);