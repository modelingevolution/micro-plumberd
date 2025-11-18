namespace MicroPlumberd.Services.ProcessManagers;

/// <summary>
/// Provides client interface for managing process managers, including subscription and retrieval operations.
/// </summary>
public interface IProcessManagerClient
{
    /// <summary>
    /// Gets the plumber instance for event store operations.
    /// </summary>
    IPlumber Plumber { get; }

    /// <summary>
    /// Gets the command bus for sending commands.
    /// </summary>
    ICommandBus Bus { get; }

    /// <summary>
    /// Retrieves or creates a process manager instance for the specified command recipient.
    /// </summary>
    /// <typeparam name="TProcessManager">The type of the process manager.</typeparam>
    /// <param name="commandRecipientId">The identifier of the command recipient.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the process manager instance.</returns>
    Task<TProcessManager> GetManager<TProcessManager>(Guid commandRecipientId)
        where TProcessManager : IProcessManager, ITypeRegister;

    /// <summary>
    /// Subscribes a process manager to handle events and commands persistently.
    /// </summary>
    /// <typeparam name="TProcessManager">The type of the process manager.</typeparam>
    /// <returns>A task that represents the asynchronous operation. The task result contains an async disposable for managing the subscription lifecycle.</returns>
    Task<IAsyncDisposable> SubscribeProcessManager<TProcessManager>() where TProcessManager : IProcessManager, ITypeRegister;
}
