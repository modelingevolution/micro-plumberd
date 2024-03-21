using EventStore.Client;
using MicroPlumberd.DirectConnect;
using Microsoft.Extensions.DependencyInjection;

namespace MicroPlumberd.Services;

public static class PlumberExtensions
{
    public static Task<IAsyncDisposable> SubscribeCommandHandler<TCommandHandler>(this IPlumber plumber) where TCommandHandler:ICommandHandler, IServiceTypeRegister
    {
        var commandHandlerType = typeof(TCommandHandler);
        var servicesConventions = plumber.Config.Conventions.ServicesConventions();
        var outputStream = servicesConventions.OutputSteamNameFromCommandHandlerConvention(commandHandlerType);
        var groupName = servicesConventions.GroupNameFromCommandHandlerConvention(commandHandlerType);
        var executor = plumber.Config.ServiceProvider.GetRequiredService<CommandHandlerExecutor<TCommandHandler>>();
        var persistently = plumber.Config.Conventions.ServicesConventions().IsHandlerExecutionPersistent(typeof(TCommandHandler));
        if (persistently)
            return plumber.SubscribeEventHandlerPersistently(executor, outputStream, groupName, startFrom: StreamPosition.End);
        else return plumber.SubscribeEventHandler(executor, outputStream, FromStream.End);
    }
   
}