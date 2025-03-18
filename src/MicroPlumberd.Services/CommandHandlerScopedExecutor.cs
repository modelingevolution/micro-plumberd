using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Net;
using EventStore.Client;
using MicroPlumberd.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MicroPlumberd.Services;

static class CommandHandlerExecutor
{
    /// <summary>
    /// Creates a new concrete instance of the CommandHandlerScopedExecutor.
    /// </summary>
    /// <param name="plumber">plumber instance</param>
    /// <param name="t">Type fo the handler</param>
    /// <returns></returns> <summary>
    public static IEventHandler CreateScoped(PlumberEngine plumber, Type t)
    {
        var executorType = typeof(CommandHandlerScopedExecutor<>).MakeGenericType(t);
        return (IEventHandler)plumber.Config.ServiceProvider.GetRequiredService(executorType);
    }
    /// <summary>
    /// Creates a new concrete instance of the CommandHandlerScopedExecutor.
    /// </summary>
    /// <param name="plumber">plumber instance</param>
    /// <param name="t">Type fo the handler</param>
    /// <returns></returns> <summary>
    public static IEventHandler CreateSingleton(IPlumber plumber, Type t)
    {
        var executorType = typeof(CommandHandlerSingletonExecutor<>).MakeGenericType(t);
        return (IEventHandler)plumber.Config.ServiceProvider.GetRequiredService(executorType);
    }
}
public static class OperationServiceContextProperties
{
    public static readonly OperationContextProperty SessionId = "SessionId";
    public static readonly OperationContextProperty RecipientId = "RecipientId";

    public static readonly OperationContextProperty CommandHandler =
        new OperationContextProperty("CommandHandler", false);

    public static readonly OperationContextProperty CommandId = new OperationContextProperty("CommandId", false);
    public static readonly OperationContextProperty Command = new OperationContextProperty("Command", false);
    public static readonly OperationContextProperty CommandName = new OperationContextProperty("CommandName", false);

}
public static class StandardOperationContextExtensions
{
   
    
    internal static void OnCommandHandlerBegin(OperationContext context) { }
    internal static void OnCommandHandlerEnd(OperationContext context) { }

    internal static void OnEventHandlerBegin(OperationContext context) { }
    internal static void OnEventHandlerEnd(OperationContext context) { }

    public static Guid? GetSessionId(this OperationContext context) => context.TryGetValue<Guid>(OperationServiceContextProperties.SessionId, out var id) ? id : null;

       public static void SetSessionId(this OperationContext context, Guid? id)
    {
        if (id.HasValue)
            context.SetValue(OperationServiceContextProperties.SessionId, id.Value);
    }
    public static string? GetRecipientId(this OperationContext context) => context.TryGetValue<string>(OperationServiceContextProperties.RecipientId, out var id) ? id : null;

    public static void SetRecipientId(this OperationContext context, string? id)
    {
        if (!string.IsNullOrEmpty(id))
            context.SetValue(OperationServiceContextProperties.RecipientId, id);
    }
    public static T? GetCommandHandler<T>(this OperationContext context) => context.TryGetValue<T>(OperationServiceContextProperties.CommandHandler, out var handler) ? handler : default(T);

    public static void SetCommandHandler(this OperationContext context, object? handler)
    {
        if (handler != null)
            context.SetValue(OperationServiceContextProperties.CommandHandler, handler);
    }
    public static Guid? GetCommandId(this OperationContext context) => context.TryGetValue<Guid>(OperationServiceContextProperties.CommandId, out var id) ? id : null;

    public static void SetCommand(this OperationContext context, object cmd)
    {
        if (cmd != null)
            context.SetValue(OperationServiceContextProperties.Command, cmd);
    }
    public static void SetCommandId(this OperationContext context, Guid? id)
    {
        if (id.HasValue)
            context.SetValue(OperationServiceContextProperties.CommandId, id.Value);
    }
    public static string? GetCommandName(this OperationContext context) => context.TryGetValue<string>(OperationServiceContextProperties.CommandName, out var name) ? name : null;
    public static T? GetCommand<T>(this OperationContext context) => context.TryGetValue<T>(OperationServiceContextProperties.Command, out var obj) ? obj : default;

    public static void SetCommandName(this OperationContext context, string? name)
    {
        if (!string.IsNullOrEmpty(name))
            context.SetValue(OperationServiceContextProperties.CommandName, name);
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

class CommandHandlerScopedExecutor<THandler>(PlumberEngine plumber, 
    ILogger<CommandHandlerScopedExecutor<THandler>> log) : IEventHandler, ITypeRegister
    where THandler:ICommandHandler, IServiceTypeRegister
{
    private readonly IServicesConvention _serviceConventions = plumber.Config.Conventions.ServicesConventions();
    class Invoker<TCommand>(CommandHandlerScopedExecutor<THandler> parent) : IInvoker { public async Task Handle(Metadata m, object ev) => 
        await parent.Handle<TCommand>(m, (TCommand)ev); }
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
        //var cmdId = m.CausationId() ?? m.EventId;//(command is IId id) ? id.Uuid : m.EventId;
        var id = IdDuckTyping.Instance.GetId(command);
        var cmdId = id is Guid g ? g : Guid.Parse(id.ToString());

        var operationContext = OperationContext.Current ?? throw new InvalidOperationException("Operation context not set!");
        operationContext.SetRecipientId(recipientId);
        operationContext.SetSessionId(sessionId);
        operationContext.SetCommandHandler(ch);
        operationContext.SetCommandName(cmdName);
        operationContext.SetCommandId(cmdId);
        
        Stopwatch sw = new Stopwatch();
        try
        {
            sw.Start();
            await ch.Execute(recipientId, command);
            log.LogDebug("Command {CommandType} executed.", command.GetType().Name);
            var evt = new CommandExecuted() { CommandId = cmdId, Duration = sw.Elapsed };
            var evtName = $"{cmdName}Executed";

            await plumber.AppendEventToStream(operationContext,cmdStream, evt, StreamState.Any, evtName);
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
            await plumber.AppendEventToStream(operationContext,cmdStream, evt, StreamState.Any, evtName);
        }
        catch (FaultException ex)
        {
            var faultData = ex.GetFaultData();
            var evt = CommandFailed.Create(cmdId, ex.Message, sw.Elapsed, (HttpStatusCode)ex.Code, faultData);
            var evtName = $"{cmdName}Failed<{faultData.GetType().Name}>";
            await plumber.AppendEventToStream(operationContext,cmdStream, evt, StreamState.Any, evtName);
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
            await plumber.AppendEventToStream(operationContext,cmdStream, evt, StreamState.Any, evtName);
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

class CommandHandlerSingletonExecutor<THandler>(IPlumber plumber, ILogger<CommandHandlerSingletonExecutor<THandler>> log) : IEventHandler, ITypeRegister
    where THandler : ICommandHandler, IServiceTypeRegister
{
    private readonly IServicesConvention _serviceConventions = plumber.Config.Conventions.ServicesConventions();
    class Invoker<TCommand>(CommandHandlerSingletonExecutor<THandler> parent) : IInvoker
    {
        public async Task Handle(Metadata m, object ev) =>
        await parent.Handle<TCommand>(m, (TCommand)ev);
    }
    interface IInvoker { Task Handle(Metadata m, object ev); }

    private readonly ConcurrentDictionary<Type, IInvoker> _cached = new();
    private readonly ConcurrentDictionary<Type, ICommandHandler> _invokers = new();
    public async Task Handle(Metadata m, object ev)
    {
        var invoker = _cached.GetOrAdd(ev.GetType(), x => (IInvoker)Activator.CreateInstance(typeof(Invoker<>).MakeGenericType(typeof(THandler), ev.GetType()), this)!);
        if (_serviceConventions.CommandHandlerSkipFilter(m, ev))
            return;
        await invoker.Handle(m, ev);
    }


    private async Task Handle<TCommand>(Metadata m, TCommand command)
    {
        var ch = (ICommandHandler<TCommand>)_invokers.GetOrAdd(typeof(TCommand),
            x => plumber.Config.ServiceProvider.GetRequiredService<ICommandHandler<TCommand>>());

       

        var recipientId = m.RecipientId();
        var sessionId = m.SessionId() ?? Guid.Empty;
        if (sessionId == Guid.Empty) return;

        var cmdStream = _serviceConventions.SessionOutStreamFromSessionIdConvention(sessionId);
        var cmdName = _serviceConventions.CommandNameConvention(command.GetType());
        //var cmdId = m.CausationId() ?? m.EventId;//(command is IId id) ? id.Uuid : m.EventId;
        var id = IdDuckTyping.Instance.GetId(command);
        var cmdId = id is Guid g ? g : Guid.Parse(id.ToString());

        var operationContext = OperationContext.Current ?? throw new InvalidOperationException("Operation context not set!");
        operationContext.SetRecipientId(recipientId);
        operationContext.SetSessionId(sessionId);
        operationContext.SetCommandHandler(ch);
        operationContext.SetCommandName(cmdName);
        operationContext.SetCommandId(cmdId);

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
            log.LogDebug(ex, "Command {CommandType}Failed<{FaultType}> appended to session steam {CommandStream}.",
                command.GetType().Name,
                faultData.GetType().Name,
                cmdStream);
        }
        catch (Exception ex)
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
            log.LogDebug(ex, "Command {CommandType}Failed appended to session steam {CommandStream}.", command.GetType().Name,
                cmdStream);
        }
    }

    public static IServiceCollection RegisterHandlers(IServiceCollection services)
    {
        return THandler.RegisterHandlers(services);
    }

    static IEnumerable<Type> ITypeRegister.Types => THandler.CommandTypes;
}