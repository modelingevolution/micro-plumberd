using System.Runtime.CompilerServices;
using EventStore.Client;
using MicroPlumberd.Api;
using Microsoft.Extensions.DependencyInjection;
namespace MicroPlumberd.Services;

/// <summary>
/// Provides extension methods for the PlumberEngine to subscribe command handlers.
/// </summary>
public static class PlumberExtensions
{
    /// <summary>
    /// Subscribes a command handler to process commands from the event stream.
    /// </summary>
    /// <typeparam name="TCommandHandler">The type of command handler to subscribe.</typeparam>
    /// <param name="plumber">The plumber engine instance.</param>
    /// <param name="subscribePersistently">
    /// Optional value indicating whether to use persistent subscriptions.
    /// If null, the value is determined by the convention settings.
    /// </param>
    /// <param name="streamStartPosition">
    /// Optional stream position to start reading from.
    /// If null, defaults to the end of the stream.
    /// </param>
    /// <returns>A task representing the asynchronous subscription operation.</returns>
    public static Task<IAsyncDisposable> SubscribeCommandHandler<TCommandHandler>(this PlumberEngine plumber, bool? subscribePersistently, StreamPosition? streamStartPosition) where TCommandHandler:ICommandHandler, IServiceTypeRegister
    {
        var commandHandlerType = typeof(TCommandHandler);
        var servicesConventions = plumber.Config.Conventions.ServicesConventions();
        var outputStream = servicesConventions.OutputSteamNameFromCommandHandlerConvention(commandHandlerType);
        var groupName = servicesConventions.GroupNameFromCommandHandlerConvention(commandHandlerType);
        var executor = plumber.Config.ServiceProvider.GetRequiredService<EventHandlerRootExecutor<TCommandHandler>>();
        var persistently = subscribePersistently ?? plumber.Config.Conventions.ServicesConventions().IsHandlerExecutionPersistent(typeof(TCommandHandler));
        if (persistently)
            return plumber.SubscribeEventHandlerPersistently(executor, outputStream, groupName, startFrom: streamStartPosition ?? StreamPosition.End);
        else return plumber.SubscribeEventHandler(executor, outputStream, FromStream.End);
    }


}