using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Net;
using EventStore.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MicroPlumberd.Services;

static class CommandHandlerExecutor
{
    public static IEventHandler Create(IPlumber plumber, Type t)
    {
        var executorType = typeof(CommandHandlerExecutor<>).MakeGenericType(t);
        return (IEventHandler)plumber.Config.ServiceProvider.GetRequiredService(executorType);
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

class CommandHandlerExecutor<T>(IPlumber plumber, ILogger<CommandHandlerExecutor<T>> log) : IEventHandler, ITypeRegister
    where T:ICommandHandler, IServiceTypeRegister
{
    private readonly IServicesConvention _serviceConventions = plumber.Config.Conventions.ServicesConventions();
    class Invoker<TCommand>(CommandHandlerExecutor<T> parent) : IInvoker { public async Task Handle(Metadata m, object ev) => await parent.Handle<TCommand>(m, (TCommand)ev); }
    interface IInvoker { Task Handle(Metadata m, object ev); }

    private readonly ConcurrentDictionary<Type, IInvoker> _cached = new();
    public async Task Handle(Metadata m, object ev)
    {
        var invoker = _cached.GetOrAdd(ev.GetType(), x => (IInvoker)Activator.CreateInstance(typeof(Invoker<>).MakeGenericType(typeof(T), ev.GetType()), this));
        if (_serviceConventions.CommandHandlerSkipFilter(m, ev))
            return;
        await invoker.Handle(m, ev);
    }

    private async Task Handle<T>(Metadata m, T command)
    {
        
        await using var scope = plumber.Config.ServiceProvider.CreateAsyncScope();
        var ch = (ICommandHandler<T>)scope.ServiceProvider.GetRequiredService(typeof(ICommandHandler<T>));
        var recipientId = m.RecipientId();
        var sessionId = m.SessionId() ?? Guid.Empty;
        if (sessionId == Guid.Empty) return;

        var cmdStream = _serviceConventions.SessionOutStreamFromSessionIdConvention(sessionId);
        var cmdName = _serviceConventions.CommandNameConvention(command.GetType());
        var cmdId = (command is IId id) ? id.Id : m.EventId;

        Stopwatch sw = new Stopwatch();
        try
        {
            sw.Start();
            await ch.Execute(recipientId, command);
            log.LogDebug("Command {CommandType} executed.", command.GetType().Name);
            await plumber.AppendEvent(cmdStream, StreamState.Any, $"{cmdName}Executed",
                new CommandExecuted()
                {
                    CommandId = cmdId,
                    Duration = sw.Elapsed
                });
            log.LogDebug("Command {CommandType} appended to session steam {CommandStream}.", command.GetType().Name,
                cmdStream);
        }
        catch (ValidationException ex)
        {
            await plumber.AppendEvent(cmdStream, StreamState.StreamExists, $"{cmdName}Failed", new CommandFailed()
            {
                CommandId = cmdId,
                Duration = sw.Elapsed,
                Message = ex.Message,
                Code = HttpStatusCode.BadRequest
            });
        }
        catch (FaultException ex)
        {
            var faultData = ex.GetFaultData();
            await plumber.AppendEvent(cmdStream, StreamState.StreamExists,
                $"{cmdName}Failed<{faultData.GetType().Name}>", CommandFailed.Create(cmdId, ex.Message, sw.Elapsed, (HttpStatusCode)ex.Code, faultData));
            log.LogDebug(ex,"Command {CommandType}Failed<{FaultType}> appended to session steam {CommandStream}.", 
                command.GetType().Name,
                faultData.GetType().Name,
                cmdStream);
        }
        catch(Exception ex)
        {
            await plumber.AppendEvent(cmdStream, StreamState.StreamExists,
                $"{cmdName}Failed", new CommandFailed()
                {
                    CommandId = cmdId,
                    Duration = sw.Elapsed,
                    Message = ex.Message,
                    Code = HttpStatusCode.InternalServerError
                });
            log.LogDebug(ex,"Command {CommandType}Failed appended to session steam {CommandStream}.", command.GetType().Name,
                cmdStream);
        }
    }
    
    public static IServiceCollection RegisterHandlers(IServiceCollection services)
    {
        return T.RegisterHandlers(services);
    }
    
    static IEnumerable<Type> ITypeRegister.Types => T.CommandTypes;
}