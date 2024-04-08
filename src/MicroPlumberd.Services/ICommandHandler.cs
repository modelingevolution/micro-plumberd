namespace MicroPlumberd.Services;

public interface ICommandHandler
{
    Task<object?> Execute(string id, object command);
}
public interface ICommandHandler<in TCommand> : ICommandHandler
{
    Task<object?> ICommandHandler.Execute(string id, object command) => Execute(id, (TCommand)command);
    Task<object?> Execute(string id, TCommand command);
}
public interface ICommandHandler<in TId, in TCommand> : ICommandHandler<TCommand>
where TId:IParsable<TId>
{
    Task<object?> Execute(TId id, TCommand command);

    Task<object?> ICommandHandler<TCommand>.Execute(string id, TCommand command) => Execute(TId.Parse(id,null), command);
}