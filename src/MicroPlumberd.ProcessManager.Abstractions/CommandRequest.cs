namespace MicroPlumberd;

public static class CommandRequest
{
    public static ICommandRequest Create(Guid recipientId, object command)
    {
        var t = typeof(CommandRequest<>).MakeGenericType(command.GetType());
        return (ICommandRequest)Activator.CreateInstance(t, recipientId, command);
    }
    public static ICommandRequest<TCommand> Create<TCommand>(Guid recipientId, TCommand command)
    {
        return new CommandRequest<TCommand>(recipientId, command);
    }
}
public record CommandRequest<TCommand> : ICommandRequest<TCommand>
{
    internal CommandRequest(Guid recipientId, TCommand command)
    {
        this.RecipientId = recipientId;
        this.Command = command;
    }

    public CommandRequest() { }
    public TCommand Command { get; init; }
    public Guid RecipientId { get; init; }
    object ICommandRequest.Command => Command;
}
