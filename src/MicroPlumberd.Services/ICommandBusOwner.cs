namespace MicroPlumberd.Services;

/// <summary>
/// Represents a command bus owner that manages the lifecycle of a command bus instance and provides command sending capabilities.
/// </summary>
public interface ICommandBusOwner : IDisposable
{
    /// <summary>
    /// Sends a command asynchronously to the specified recipient.
    /// </summary>
    /// <param name="recipientId">The ID of the recipient that should handle the command.</param>
    /// <param name="command">The command to send.</param>
    /// <param name="timeout">The optional timeout for command execution. If not specified, the default timeout from configuration will be used.</param>
    /// <param name="fireAndForget">If true, the method returns immediately without waiting for command execution to complete. If false, waits for command execution result.</param>
    /// <param name="token">A cancellation token to observe while waiting for the command to complete.</param>
    /// <returns>A task representing the asynchronous send operation.</returns>
    Task SendAsync(object recipientId, object command, TimeSpan? timeout = null, bool fireAndForget = false,
        CancellationToken token = default);
}