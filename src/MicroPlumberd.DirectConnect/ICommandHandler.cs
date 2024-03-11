namespace MicroPlumberd.DirectConnect;

public interface ICommandHandler<in TCommand>
    where TCommand : ICommand
{
    Task<object> Execute(Guid id, TCommand command);
}