using System.Diagnostics;
using ModelingEvolution.DirectConnect;

namespace MicroPlumberd.DirectConnect;

internal class CommandHandlerCorrelationDecorator<TCommand>(IRequestHandler<CommandEnvelope<TCommand>, object> next) 
    : IRequestHandler<CommandEnvelope<TCommand>, object>
    
{
    public async Task<object> Handle(CommandEnvelope<TCommand> request)
    {
        if (OperationContext.Current == null)
            throw new NullReferenceException("Operation context needs to be set when used with decorator");

        OperationContext.Current.SetCorrelationId(request.CorrelationId ?? request.CommandId);
        OperationContext.Current.SetCausationId(request.CommandId);
        //Debug.WriteLine($"===> Setting scope of causation id to: {request.CommandId}");
        return await next.Handle(request);
    }
}