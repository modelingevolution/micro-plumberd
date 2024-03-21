// ReSharper disable once CheckNamespace
namespace MicroPlumberd;

public interface IProcessAction
{

}

public record StateChangeAction<TOwner>(Guid Id, long Version, params object[] Events) : IStateChangeAction
{
    public Type Owner => typeof(TOwner);
}
public interface IStateChangeAction : IProcessAction
{
    Guid Id { get;  }
    long Version { get; }
    object[] Events { get; }
    Type Owner { get; }
}
public interface ICommandRequest : IProcessAction
{
    Guid RecipientId { get; }
    object Command { get; }
}
public interface ICommandRequest<out TCommand> : ICommandRequest
{
    new TCommand Command { get; }
}