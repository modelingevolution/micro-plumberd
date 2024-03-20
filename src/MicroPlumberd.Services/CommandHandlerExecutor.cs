using System.Collections.Concurrent;
using System.Diagnostics;
using EventStore.Client;
using MicroPlumberd.DirectConnect;
using Microsoft.Extensions.DependencyInjection;

namespace MicroPlumberd.Services;

static class CommandHandlerExecutor
{
    public static IEventHandler Create(IPlumber plumber, Type t)
    {
        return (IEventHandler)Activator.CreateInstance(typeof(CommandHandlerExecutor<>).MakeGenericType(t), plumber)!;
    }
}

public static class MetadataExtensions
{
    public static Guid? SessionId(this Metadata m)
    {
        if (m.Data.TryGetProperty("SessionId", out var v))
            return Guid.Parse(v.GetString()!);
        return null;
    }
}

class CommandHandlerExecutor<T>(IPlumber plumber) : IEventHandler, ITypeRegister
    where T:ICommandHandler, IServiceTypeRegister
{
    private IServicesConvention _serviceConventions = plumber.Config.Conventions.ServicesConventions();
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
        var ch = (ICommandHandler<T>)scope.ServiceProvider.GetRequiredService(typeof(ICommandHandler<T>));
        var recipientId = m.RecipientId();
        var sessionId = m.SessionId() ?? Guid.Empty;
        if (sessionId == Guid.Empty) return;

        var cmdStream = _serviceConventions.SessionStreamFromSessionIdConvention(sessionId);
        var cmdName = _serviceConventions.CommandNameConvention(command.GetType());
        var cmdId = (command is IId id) ? id.Id : m.EventId;

        Stopwatch sw = new Stopwatch();
        try
        {
            sw.Start();
            await ch.Execute(recipientId, command);
            //var t = Task.Run(async () => await ch.Execute(recipientId, command));
            //await t.WaitAsync(TimeSpan.FromSeconds(110));
            await plumber.AppendEvent(cmdStream, StreamState.StreamExists, $"{cmdName}Executed",
                new CommandExecuted()
                {
                    CommandId = cmdId,
                    Duration = sw.Elapsed
                });
        }
        catch (CommandFaultException ex)
        {
            var faultData = ex.GetFaultData();
            await plumber.AppendEvent(cmdStream, StreamState.StreamExists,
                $"{cmdName}Failed<{faultData.GetType().Name}>", CommandFailed.Create(cmdId, ex.Message, sw.Elapsed,faultData));
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