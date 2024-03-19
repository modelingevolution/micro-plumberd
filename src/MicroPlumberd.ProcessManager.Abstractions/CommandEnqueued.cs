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

public sealed class CommandEnqueued<TCommand>(Guid recipientId, TCommand command) : ICommandEnqueued
{
    object ICommandEnqueued.Command => command;
    public TCommand Command => command;
    public Guid RecipientId => recipientId;
}