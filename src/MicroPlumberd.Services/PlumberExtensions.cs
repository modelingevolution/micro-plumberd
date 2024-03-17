using MicroPlumberd.DirectConnect;

namespace MicroPlumberd.Services;

public static class PlumberExtensions
{
    public static Task<IAsyncDisposable> SubscribeCommandHandler<TCommandHandler>(this IPlumber plumber) where TCommandHandler:ICommandHandler, IServiceTypeRegister
    {
        var outputStream = plumber.Config.Conventions.GetCommandHandlerOutputStreamName<TCommandHandler>();
        var groupName= plumber.Config.Conventions.GetCommandHandlerGroupName<TCommandHandler>();
        return plumber.SubscribeEventHandlerPersistently(new CommandHandlerDispatcher<TCommandHandler>(plumber), 
            outputStream, groupName);
    }
   
}