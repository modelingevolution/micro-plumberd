namespace MicroPlumberd.Services;

/// <summary>
/// Defines a contract for starting event handler subscriptions.
/// </summary>
interface IEventHandlerStarter
{
    /// <summary>
    /// Starts the event handler subscription.
    /// </summary>
    /// <param name="stoppingToken">A cancellation token to stop the subscription.</param>
    /// <returns>A task representing the asynchronous start operation.</returns>
    Task Start(CancellationToken stoppingToken);
}
/// <summary>
/// Defines a contract for providing command handler startup metadata.
/// </summary>
interface ICommandHandlerStarter
{
    /// <summary>
    /// Gets the collection of command types that the handler can process.
    /// </summary>
    IEnumerable<Type> CommandTypes { get; }
    /// <summary>
    /// Gets the type of the command handler.
    /// </summary>
    Type HandlerType { get; }
    /// <summary>
    /// Gets a value indicating whether the handler is scoped.
    /// </summary>
    bool Scoped { get; }
}