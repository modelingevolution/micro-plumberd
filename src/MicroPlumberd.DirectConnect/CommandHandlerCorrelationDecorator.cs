using ModelingEvolution.DirectConnect;

namespace MicroPlumberd.DirectConnect;

internal class CommandHandlerCorrelationDecorator<TCommand>(IRequestHandler<CommandEnvelope<TCommand>, object> next) 
    : IRequestHandler<CommandEnvelope<TCommand>, object>
    where TCommand:ICommand
{
    public async Task<object> Handle(CommandEnvelope<TCommand> request)
    {
        InvocationContext.Current.SetCorrelation(request.Command.Id);
        InvocationContext.Current.SetCausation(request.Command.Id);
        return await next.Handle(request);
    }
}