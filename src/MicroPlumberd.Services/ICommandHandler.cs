namespace MicroPlumberd.DirectConnect;

public interface ICommandHandler<in TCommand>
{
    Task<object> Execute(Guid id, TCommand command);
}