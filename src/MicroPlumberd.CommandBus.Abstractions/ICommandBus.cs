namespace MicroPlumberd;

/// <summary>
/// Represents a command bus for sending commands.
/// </summary>
public interface ICommandBus : IAsyncDisposable
{
    /// <summary>
    /// Sends a command synchronously to the specified recipient - command handler. It waits for the response.
    /// </summary>
    /// <param name="recipientId">The ID of the recipient.</param>
    /// <param name="command">The command to send.</param>
    /// <param name="token"></param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SendAsync(object recipientId, object command, TimeSpan? timeout = null, bool fireAndForget = false, CancellationToken token = default);

    /// <summary>
    /// Queues a command async to be processed by the specified recipient - command handler, in another session. Timeout id be default disabled. the default is fire and forget.
    /// </summary>
    /// <param name="recipientId">The ID of the recipient to process the command.</param>
    /// <param name="command">The command to be queued for processing.</param>
    /// <param name="token">A <see cref="CancellationToken"/> to observe while waiting for the task to complete.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// This method queues the command for later processing, ensuring it is sent to the appropriate recipient.
    /// </remarks>
    Task QueueAsync(object recipientId, object command, TimeSpan? timeout = null, bool fireAndForget = true,  CancellationToken token = default);
}