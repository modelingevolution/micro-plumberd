using System.Net;
using EventStore.Client;
using MicroPlumberd.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelingEvolution.DirectConnect;

namespace MicroPlumberd.DirectConnect;

internal class CommandHandlerCore<TCommand>(IServiceProvider serviceProvider) : IRequestHandler<CommandEnvelope<TCommand>, object>
{
    public async Task<object> Handle(CommandEnvelope<TCommand> request)
    {
        await using var sp = serviceProvider.CreateAsyncScope();

        var ch = sp.ServiceProvider.GetRequiredService<ICommandHandler<TCommand>>();
        try
        {
            return await ch.Execute(request.StreamId, request.Command);
        }
        catch (FaultException ex)
        {
            var faultData = ex.GetFaultData();
            return FaultEnvelope.Create(faultData, ex.Message);
        }
        catch (Exception ex)
        {
            return new HandlerOperationStatus()
            {
                Code = HttpStatusCode.InternalServerError,
                Error = ex.Message
            };
        }
    }
}