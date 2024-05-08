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
    /// <summary>
    /// Creates a new concrete instance of the CommandHandlerExecutor.
    /// </summary>
    /// <param name="plumber">plumber instance</param>
    /// <param name="t">Type fo the handler</param>
    /// <returns></returns> <summary>
    public static IEventHandler Create(IPlumber plumber, Type t)
    {
        var executorType = typeof(CommandHandlerExecutor<>).MakeGenericType(t);
        return (IEventHandler)plumber.Config.ServiceProvider.GetRequiredService(executorType);
    }
}

/// <summary>
/// Provides extension methods for working with metadata.
/// </summary>
public static class MetadataExtensions
{
    /// <summary>
    /// Retrieves the session ID from the metadata.
    /// </summary>
    /// <param name="m">The metadata.</param>
    /// <returns>The session ID, or null if not found.</returns>
    public static Guid? SessionId(this Metadata m)
    {
        if (m.Data.TryGetProperty("SessionId", out var v))
            return Guid.Parse(v.GetString()!);
        return null;
    }
}

class CommandHandlerExecutor<THandler>(IPlumber plumber, ILogger<CommandHandlerExecutor<THandler>> log) : IEventHandler, ITypeRegister
    where THandler:ICommandHandler, IServiceTypeRegister
{
    private readonly IServicesConvention _serviceConventions = plumber.Config.Conventions.ServicesConventions();
    class Invoker<TCommand>(CommandHandlerExecutor<THandler> parent) : IInvoker { public async Task Handle(Metadata m, object ev) => await parent.Handle<TCommand>(m, (TCommand)ev); }
    interface IInvoker { Task Handle(Metadata m, object ev); }

    private readonly ConcurrentDictionary<Type, IInvoker> _cached = new();
    public async Task Handle(Metadata m, object ev)
    {
        var invoker = _cached.GetOrAdd(ev.GetType(), x => (IInvoker)Activator.CreateInstance(typeof(Invoker<>).MakeGenericType(typeof(THandler), ev.GetType()), this)!);
        if (_serviceConventions.CommandHandlerSkipFilter(m, ev))
            return;
        await invoker.Handle(m, ev);
    }

    private async Task Handle<TCommand>(Metadata m, TCommand command)
    {
        
        await using var scope = plumber.Config.ServiceProvider.CreateAsyncScope();
        var ch = (ICommandHandler<TCommand>)scope.ServiceProvider.GetRequiredService(typeof(ICommandHandler<TCommand>));
        var recipientId = m.RecipientId();
        var sessionId = m.SessionId() ?? Guid.Empty;
        if (sessionId == Guid.Empty) return;

        var cmdStream = _serviceConventions.SessionOutStreamFromSessionIdConvention(sessionId);
        var cmdName = _serviceConventions.CommandNameConvention(command.GetType());
        var cmdId = m.CausationId() ?? m.EventId;//(command is IId id) ? id.Uuid : m.EventId;

        Stopwatch sw = new Stopwatch();
        try
        {
            sw.Start();
            await ch.Execute(recipientId, command);
            log.LogDebug("Command {CommandType} executed.", command.GetType().Name);
            var evt = new CommandExecuted() { CommandId = cmdId, Duration = sw.Elapsed };
            var evtName = $"{cmdName}Executed";
            await plumber.AppendEventToStream(cmdStream, evt, StreamState.Any, evtName);
            log.LogDebug("Command {CommandType} appended to session steam {CommandStream}.", command.GetType().Name,
                cmdStream);
        }
        catch (ValidationException ex)
        {
            var evt = new CommandFailed()
            {
                CommandId = cmdId,
                Duration = sw.Elapsed,
                Message = ex.Message,
                Code = HttpStatusCode.BadRequest
            };
            var evtName = $"{cmdName}Failed";
            await plumber.AppendEventToStream(cmdStream, evt, StreamState.Any, evtName);
        }
        catch (FaultException ex)
        {
            var faultData = ex.GetFaultData();
            var evt = CommandFailed.Create(cmdId, ex.Message, sw.Elapsed, (HttpStatusCode)ex.Code, faultData);
            var evtName = $"{cmdName}Failed<{faultData.GetType().Name}>";
            await plumber.AppendEventToStream(cmdStream, evt, StreamState.Any, evtName);
            log.LogDebug(ex,"Command {CommandType}Failed<{FaultType}> appended to session steam {CommandStream}.", 
                command.GetType().Name,
                faultData.GetType().Name,
                cmdStream);
        }
        catch(Exception ex)
        {
            var evt = new CommandFailed()
            {
                CommandId = cmdId,
                Duration = sw.Elapsed,
                Message = ex.Message,
                Code = HttpStatusCode.InternalServerError
            };
            var evtName = $"{cmdName}Failed";
            await plumber.AppendEventToStream(cmdStream, evt, StreamState.Any, evtName);
            log.LogDebug(ex,"Command {CommandType}Failed appended to session steam {CommandStream}.", command.GetType().Name,
                cmdStream);
        }
    }
    
    public static IServiceCollection RegisterHandlers(IServiceCollection services)
    {
        return THandler.RegisterHandlers(services);
    }
    
    static IEnumerable<Type> ITypeRegister.Types => THandler.CommandTypes;
}