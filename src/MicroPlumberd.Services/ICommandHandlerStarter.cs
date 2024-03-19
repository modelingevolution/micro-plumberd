namespace MicroPlumberd.Services;

interface ICommandHandlerStarter
{
    Task Start();
    IEnumerable<Type> CommandTypes { get; }
    Type HandlerType { get; }
}