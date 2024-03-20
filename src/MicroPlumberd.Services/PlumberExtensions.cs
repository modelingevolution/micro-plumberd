using MicroPlumberd.DirectConnect;

namespace MicroPlumberd.Services;

public static class PlumberExtensions
{
    public static Task<IAsyncDisposable> SubscribeCommandHandler<TCommandHandler>(this IPlumber plumber) where TCommandHandler:ICommandHandler, IServiceTypeRegister
    {
        var commandHandlerType = typeof(TCommandHandler);
        var servicesConventions = plumber.Config.Conventions.ServicesConventions();
        var outputStream = servicesConventions.OutputSteamNameFromCommandHandlerConvention(commandHandlerType);
        var groupName = servicesConventions.GroupNameFromCommandHandlerConvention(commandHandlerType);
        return plumber.SubscribeEventHandlerPersistently(new CommandHandlerExecutor<TCommandHandler>(plumber), 
            outputStream, groupName);
    }
   
}