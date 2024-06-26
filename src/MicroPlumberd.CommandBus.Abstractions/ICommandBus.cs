﻿namespace MicroPlumberd;

/// <summary>
/// Represents a command bus for sending commands.
/// </summary>
public interface ICommandBus
{
    /// <summary>
    /// Sends a command asynchronously to the specified recipient - command handler.
    /// </summary>
    /// <param name="recipientId">The ID of the recipient.</param>
    /// <param name="command">The command to send.</param>
    /// <param name="token"></param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SendAsync(object recipientId, object command, CancellationToken token = default);
}