using System.ComponentModel;
using System.Security.Cryptography.X509Certificates;
using EventStore.Client;
using MicroPlumberd;
using MicroPlumberd.Services;

namespace MicroPlumberd.Services;

class CommandBus(IPlumber plumber) : ICommandBus
{
    public async Task SendAsync(Guid recipientId, object command)
    {
        var causationId = InvocationContext.Current.CausactionId();
        var correlationId = InvocationContext.Current.CorrelationId();

        var streamId = plumber.Config.Conventions.GetSteamIdFromCommand(command.GetType(), recipientId);
        var metadata = new
        {
            CorrelationId = correlationId, 
            CausationId = causationId,
            RecipientId = recipientId
        };
        await plumber.AppendEvents(streamId, StreamState.Any, [command], metadata);
    }
}