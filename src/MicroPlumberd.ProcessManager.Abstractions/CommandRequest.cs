namespace MicroPlumberd;

/// <summary>
/// Factory class for creating command request instances.
/// </summary>
public static class CommandRequest
{
    /// <summary>
    /// Creates a non-generic command request for the specified recipient and command.
    /// </summary>
    /// <param name="recipientId">The unique identifier of the command recipient aggregate.</param>
    /// <param name="command">The command object to be sent.</param>
    /// <returns>A command request instance.</returns>
    public static ICommandRequest Create(Guid recipientId, object command)
    {
        var t = typeof(CommandRequest<>).MakeGenericType(command.GetType());
        return (ICommandRequest)Activator.CreateInstance(t, recipientId, command);
    }

    /// <summary>
    /// Creates a strongly-typed command request for the specified recipient and command.
    /// </summary>
    /// <typeparam name="TCommand">The type of the command being sent.</typeparam>
    /// <param name="recipientId">The unique identifier of the command recipient aggregate.</param>
    /// <param name="command">The command to be sent.</param>
    /// <returns>A strongly-typed command request instance.</returns>
    public static ICommandRequest<TCommand> Create<TCommand>(Guid recipientId, TCommand command)
    {
        return new CommandRequest<TCommand>(recipientId, command);
    }
}

/// <summary>
/// Represents a strongly-typed command request that can be sent to a recipient aggregate.
/// </summary>
/// <typeparam name="TCommand">The type of the command being sent.</typeparam>
public record CommandRequest<TCommand> : ICommandRequest<TCommand>
{
    internal CommandRequest(Guid recipientId, TCommand command)
    {
        this.RecipientId = recipientId;
        this.Command = command;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CommandRequest{TCommand}"/> class.
    /// </summary>
    public CommandRequest() { }

    /// <summary>
    /// Gets or initializes the command to be sent.
    /// </summary>
    public TCommand Command { get; init; }

    /// <summary>
    /// Gets or initializes the unique identifier of the command recipient aggregate.
    /// </summary>
    public Guid RecipientId { get; init; }

    /// <summary>
    /// Gets the command as an object.
    /// </summary>
    object ICommandRequest.Command => Command;
}
