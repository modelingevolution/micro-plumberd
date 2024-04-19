namespace MicroPlumberd.Services;

interface IEventHandlerStarter
{
    Task Start(CancellationToken stoppingToken);
}
interface ICommandHandlerStarter
{
    IEnumerable<Type> CommandTypes { get; }
    Type HandlerType { get; }
}