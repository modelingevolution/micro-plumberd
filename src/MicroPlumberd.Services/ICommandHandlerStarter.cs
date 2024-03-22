namespace MicroPlumberd.Services;

interface IEventHandlerStarter
{
    Task Start();
}
interface ICommandHandlerStarter
{
    IEnumerable<Type> CommandTypes { get; }
    Type HandlerType { get; }
}