// ReSharper disable once CheckNamespace
namespace MicroPlumberd;

public static class CommandEnqueued
{
    public static ICommandEnqueued Create(Guid recipient, object command)
    {
        var type = typeof(CommandEnqueued<>).MakeGenericType(command.GetType());
        //TODO: Slow, should cache ctor.
        var cmd = (ICommandEnqueued)Activator.CreateInstance(type, recipient, command);

        return cmd;
    }
}

public sealed class CommandEnqueued<TCommand> : ICommandEnqueued
{
    public CommandEnqueued(Guid recipientId, TCommand command)
    {
        RecipientId = recipientId;
        Command = command;
    }

    public CommandEnqueued()
    {
        
    }
    object ICommandEnqueued.Command => Command;

    public TCommand Command { get; init; }

    public Guid RecipientId { get; init; }
}