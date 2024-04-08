// ReSharper disable once CheckNamespace
namespace MicroPlumberd;

/// <summary>
/// Factory class for <see cref="CommandEnqueued{TCommand}"/> events.
/// </summary>
public static class CommandEnqueued
{
    /// <summary>
    /// Creates the specified event.
    /// </summary>
    /// <param name="recipient">The recipient.</param>
    /// <param name="command">The command.</param>
    /// <returns></returns>
    public static ICommandEnqueued Create(Guid recipient, object command)
    {
        var type = typeof(CommandEnqueued<>).MakeGenericType(command.GetType());
        //TODO: Slow, should cache ctor.
        var cmd = (ICommandEnqueued)Activator.CreateInstance(type, recipient, command);

        return cmd;
    }
}
/// <summary>
/// Immutable class used in ProcessManagers in Given methods the rebuild the state based on send commands.
/// </summary>
/// <typeparam name="TCommand">The type of the command.</typeparam>
public sealed class CommandEnqueued<TCommand> : ICommandEnqueued
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CommandEnqueued{TCommand}"/> class.
    /// </summary>
    /// <param name="recipientId">The recipient identifier.</param>
    /// <param name="command">The command.</param>
    public CommandEnqueued(Guid recipientId, TCommand command)
    {
        RecipientId = recipientId;
        Command = command;
    }
    /// <summary>
    /// Initializes a new instance of the <see cref="CommandEnqueued{TCommand}"/> class.
    /// </summary>
    public CommandEnqueued() { }
    object ICommandEnqueued.Command => Command;

    /// <summary>
    /// Gets the command.
    /// </summary>
    /// <value>
    /// The command.
    /// </value>
    public TCommand Command { get; init; }

    /// <summary>
    /// Gets the recipient identifier.
    /// </summary>
    /// <value>
    /// The recipient identifier.
    /// </value>
    public Guid RecipientId { get; init; }
}