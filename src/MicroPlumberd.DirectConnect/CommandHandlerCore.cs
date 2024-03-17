using Microsoft.Extensions.DependencyInjection;
using ModelingEvolution.DirectConnect;

namespace MicroPlumberd.DirectConnect;

internal class CommandHandlerCore<TCommand>(IServiceProvider serviceProvider) : IRequestHandler<CommandEnvelope<TCommand>, object>
{
    public async Task<object> Handle(CommandEnvelope<TCommand> request)
    {
        await using var sp = serviceProvider.CreateAsyncScope();
        return await sp.ServiceProvider.GetRequiredService<ICommandHandler<TCommand>>().Execute(request.StreamId, request.Command);
    }
}