using System.Collections.Concurrent;
using System.Diagnostics;
using EventStore.Client;
using MicroPlumberd.DirectConnect;
using Microsoft.Extensions.DependencyInjection;

namespace MicroPlumberd.Services;

class CommandHandlerExecutor<T>(IPlumber plumber) : IEventHandler, ITypeRegister
    where T:ICommandHandler, IServiceTypeRegister
{
    class Invoker<TCommand>(CommandHandlerExecutor<T> parent) : IInvoker { public async Task Handle(Metadata m, object ev) => await parent.Handle<TCommand>(m, (TCommand)ev); }
    interface IInvoker { Task Handle(Metadata m, object ev); }

    private readonly ConcurrentDictionary<Type, IInvoker> _cached = new();
    public async Task Handle(Metadata m, object ev)
    {
        var invoker = _cached.GetOrAdd(ev.GetType(), x => (IInvoker)Activator.CreateInstance(typeof(Invoker<>).MakeGenericType(typeof(T), ev.GetType()), this));
        await invoker.Handle(m, ev);
    }

    private async Task Handle<T>(Metadata m, T command)
    {
        await using var scope = plumber.Config.ServiceProvider.CreateAsyncScope();
        var ch = (ICommandHandler)scope.ServiceProvider.GetRequiredService(typeof(ICommandHandler<T>));
        var recipientId = m.RecipientId();
        var cmdStream = plumber.Config.Conventions.GetSteamIdFromCommand<T>(recipientId);
        var cmdName = plumber.Config.Conventions.GetEventNameConvention(null,command);
        var cmdId = (command is IId id) ? id.Id : m.EventId;

        Stopwatch sw = new Stopwatch();
        try
        {
            sw.Start();
            await ch.Execute(recipientId, command);
            await plumber.AppendEvent(cmdStream, StreamState.StreamExists, $"{cmdName}Executed",
                new CommandExecuted()
                {
                    CommandId = cmdId,
                    Duration = sw.Elapsed
                });
        }
        catch(Exception ex)
        {
            await plumber.AppendEvent(cmdStream, StreamState.StreamExists,
                $"{cmdName}Failed", new CommandFailed()
                {
                    CommandId = cmdId,
                    Duration = sw.Elapsed,
                    Message = ex.Message
                });
        }
    }
    
    public static IServiceCollection RegisterHandlers(IServiceCollection services)
    {
        return T.RegisterHandlers(services);
    }
    // TODO: Conventions are not consistent.
    private static Dictionary<string, Type> _typeRegister = T.CommandTypes.ToDictionary(x => x.Name);
    public static IReadOnlyDictionary<string, Type> TypeRegister => _typeRegister;
}

record CommandExecuted
{
    public Guid CommandId { get; init; }
    public TimeSpan Duration { get; init; }
}

record CommandFailed
{
    public Guid CommandId { get; init; }
    public TimeSpan Duration { get; init; }
    public string Message { get; init; }
}
record CommandExecuted<TFault> : CommandFailed
{
    public TFault Fault { get; init; }
}
