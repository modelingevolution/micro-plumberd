using System.Collections.Concurrent;
using System.Security.Cryptography.X509Certificates;
using EventStore.Client;
using MicroPlumberd;
using MicroPlumberd.DirectConnect;
using MicroPlumberd.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MicroPlumberd.Services;

class CommandBus(Plumber plumber) : ICommandBus
{
    public async Task SendAsync(Guid recipientId, object command)
    {
        var causationId = InvocationContext.Current.CausactionId();
        var correlationId = InvocationContext.Current.CorrelationId();

        var streamId = plumber.Conventions.GetSteamIdFromCommand(command.GetType(), recipientId);
        var metadata = new
        {
            CorrelationId = correlationId, 
            CausationId = causationId,
            RecipientId = recipientId
        };
        await plumber.AppendEvents(streamId, StreamState.Any, [command], metadata);
    }
}

public static class PlumberExtensions
{
    public static Task<IAsyncDisposable> SubscribeCommandHandler<TCommandHandler>(this Plumber plumber) where TCommandHandler:ICommandHandler, IServiceTypeRegister
    {
        return plumber.SubscribeEventHandlerPersistently(new CommandHandlerDispatcher<TCommandHandler>(plumber), plumber.Conventions.GetCommandHandlerOutputStreamName<TCommandHandler>());
    }
}



record OutputSteamNameFromCommandHandlerExtension
{
    public OutputSteamNameFromCommandHandler Extension { get; set; } = x => $">{x.Name}";
}
record SteamNameFromCommandExtension
{
    public SteamIdFromCommand Extension { get; set; } = (r, c) => $">Cmd-{r}";
}


public delegate string SteamIdFromCommand(Guid recipientId, Type commandType);
public delegate string OutputSteamNameFromCommandHandler(Type commandHandlerType);
public static class PlumberdConventionsExtensions
{
    public static string GetCommandHandlerOutputStreamName<TCommandHandler>(this IConventions conventions) => conventions.GetExtension<OutputSteamNameFromCommandHandlerExtension>().Extension(typeof(TCommandHandler));
    public static void SetCommandHandlerOutputStreamName<TCommandHandler>(this IConventions conventions, OutputSteamNameFromCommandHandler cvt) => conventions.GetExtension<OutputSteamNameFromCommandHandlerExtension>().Extension = cvt;

    public static string GetSteamIdFromCommand<TCommand>(this IConventions conventions, Guid recipientId) =>
        conventions.GetSteamIdFromCommand(typeof(TCommand), recipientId);
    public static string GetSteamIdFromCommand(this IConventions conventions,Type commandType, Guid recipientId) => conventions.GetExtension<SteamNameFromCommandExtension>().Extension(recipientId, commandType);
    public static void SetSteamNameFromCommand(this IConventions conventions, SteamIdFromCommand cvt) => conventions.GetExtension<SteamNameFromCommandExtension>().Extension = cvt;
}

public static class CommandHandlerMetadataExtensions
{
    public static Guid RecipientId(this Metadata m) => m.TryGetValue<Guid>("RecipientId", out var v)
        ? v
        : throw new InvalidOperationException("RecipientId not found in command");
}


class CommandHandlerDispatcher<T>(Plumber plumber) : IEventHandler, ITypeRegister
    where T:ICommandHandler, IServiceTypeRegister
{
    private readonly ConcurrentDictionary<Type, Type> _cached = new();
    public async Task Handle(Metadata m, object ev)
    {
        var chType = _cached.GetOrAdd(ev.GetType(), x => typeof(ICommandHandler<>).MakeGenericType(ev.GetType()));
        var ch = (ICommandHandler)plumber.ServiceProvider.GetRequiredService(chType);
        await ch.Execute(m.RecipientId(), ev);
    }

    
    public static IServiceCollection RegisterHandlers(IServiceCollection services)
    {
        return T.RegisterHandlers(services);
    }
    // TODO: Conventions are not consistent.
    private static Dictionary<string, Type> _typeRegister = T.CommandTypes.ToDictionary(x => x.Name);
    public static IReadOnlyDictionary<string, Type> TypeRegister => _typeRegister;
}