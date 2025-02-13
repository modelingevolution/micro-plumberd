using System.Diagnostics;
using ModelingEvolution.DirectConnect;

namespace MicroPlumberd.DirectConnect;

internal class CommandHandlerCorrelationDecorator<TCommand>(IRequestHandler<CommandEnvelope<TCommand>, object> next) 
    : IRequestHandler<CommandEnvelope<TCommand>, object>
    
{
    public async Task<object> Handle(CommandEnvelope<TCommand> request)
    {
        InvocationContext.Current.SetCorrelation(request.CorrelationId ?? request.CommandId);
        InvocationContext.Current.SetCausation(request.CommandId);
        //Debug.WriteLine($"===> Setting scope of causation id to: {request.CommandId}");
        return await next.Handle(request);
    }
}