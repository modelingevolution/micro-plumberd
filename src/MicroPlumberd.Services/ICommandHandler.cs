namespace MicroPlumberd.Services;

public interface ICommandHandler
{
    Task<object?> Execute(Guid id, object command);
}
public interface ICommandHandler<in TCommand> : ICommandHandler
{
    Task<object?> Execute(Guid id, TCommand command);
}