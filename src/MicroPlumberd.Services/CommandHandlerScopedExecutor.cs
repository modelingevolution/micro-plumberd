using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Net;
using EventStore.Client;
using MicroPlumberd.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MicroPlumberd.Services;

/// <summary>
/// Defines the contract for executing command handlers.
/// </summary>
/// <typeparam name="THandle">The type of command handler.</typeparam>
/// <remarks>
/// TODO: Should switch to decorator pattern CommandHandler Service-&gt;Executor
///
/// Then we would have separate decorators for:
/// - retry policy
/// - validation
/// - authorization
/// - authentication
/// </remarks>
internal interface ICommandHandleExecutor<THandle>
{
    /// <summary>
    /// Handles the execution of a command.
    /// </summary>
    /// <typeparam name="TCommand">The type of command to handle.</typeparam>
    /// <param name="m">The metadata associated with the command.</param>
    /// <param name="command">The command to execute.</param>
    /// <returns>A task representing the asynchronous handle operation.</returns>
    Task Handle<TCommand>(Metadata m, TCommand command);
}

/// <summary>
/// Root event handler executor that dispatches commands to the appropriate command handler.
/// </summary>
/// <typeparam name="THandler">The type of command handler.</typeparam>
internal class EventHandlerRootExecutor<THandler>(ICommandHandleExecutor<THandler> next) : IEventHandler, ITypeRegister
    where THandler : ICommandHandler, IServiceTypeRegister
{
    /// <summary>
    /// Generic invoker for strongly-typed command handling.
    /// </summary>
    /// <typeparam name="TCommand">The type of command.</typeparam>
    class Invoker<TCommand>(ICommandHandleExecutor<THandler> parent) : IInvoker
    {
        /// <inheritdoc/>
        public async Task Handle(Metadata m, object ev) =>
            await parent.Handle<TCommand>(m, (TCommand)ev);
    }
    /// <summary>
    /// Interface for type-erased command invocation.
    /// </summary>
    interface IInvoker {
        /// <summary>
        /// Handles a command event.
        /// </summary>
        /// <param name="m">The metadata.</param>
        /// <param name="ev">The event.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task Handle(Metadata m, object ev);
    }

    private readonly ConcurrentDictionary<Type, IInvoker> _cached = new();
    /// <inheritdoc/>
    public async Task Handle(Metadata m, object ev)
    {
        var invoker = _cached.GetOrAdd(ev.GetType(), x => (IInvoker)Activator.CreateInstance(typeof(Invoker<>).MakeGenericType(typeof(THandler), ev.GetType()), next)!);
        
        await invoker.Handle(m, ev);
    }

    /// <summary>
    /// Gets the collection of command types supported by this handler.
    /// </summary>
    public static IEnumerable<Type> Types => THandler.CommandTypes;
}

/// <summary>
/// Factory class for creating command handler executors.
/// </summary>
static class CommandHandlerExecutor
{
    /// <summary>
    /// Creates a chain for command handler executors.
    /// </summary>
    /// <param name="plumber">The plumber instance.</param>
    /// <param name="t">The type of the handler.</param>
    /// <returns>An event handler that wraps the command handler execution logic.</returns>
    public static IEventHandler Create(PlumberEngine plumber, Type t)
    {
        var executorType = typeof(EventHandlerRootExecutor<>).MakeGenericType(t);
        return (IEventHandler)plumber.Config.ServiceProvider.GetRequiredService(executorType);
    }
  
}
/// <summary>
/// Provides operation context properties specific to command and event handling in the services layer.
/// </summary>
public static class OperationServiceContextProperties
{
    /// <summary>
    /// Gets the operation context property key for the session ID.
    /// </summary>
    public static readonly OperationContextProperty SessionId = "SessionId";

    /// <summary>
    /// Gets the operation context property key for the recipient ID.
    /// </summary>
    public static readonly OperationContextProperty RecipientId = "RecipientId";

    /// <summary>
    /// Gets the operation context property key for the command handler instance.
    /// </summary>
    public static readonly OperationContextProperty CommandHandler =
        new OperationContextProperty("CommandHandler", false);

    /// <summary>
    /// Gets the operation context property key for the command ID.
    /// </summary>
    public static readonly OperationContextProperty CommandId = new OperationContextProperty("CommandId", false);

    /// <summary>
    /// Gets the operation context property key for the command object.
    /// </summary>
    public static readonly OperationContextProperty Command = new OperationContextProperty("Command", false);

    /// <summary>
    /// Gets the operation context property key for the command name.
    /// </summary>
    public static readonly OperationContextProperty CommandName = new OperationContextProperty("CommandName", false);

}

/// <summary>
/// Provides extension methods for working with operation context in command and event handling scenarios.
/// </summary>
public static class StandardOperationContextExtensions
{


    internal static void OnCommandHandlerBegin(OperationContext context) { }
    internal static void OnCommandHandlerEnd(OperationContext context) { }

    internal static void OnEventHandlerBegin(OperationContext context) { }
    internal static void OnEventHandlerEnd(OperationContext context) { }

    /// <summary>
    /// Retrieves the session ID from the operation context.
    /// </summary>
    /// <param name="context">The operation context.</param>
    /// <returns>The session ID if present; otherwise, null.</returns>
    public static Guid? GetSessionId(this OperationContext context) => context.TryGetValue<Guid>(OperationServiceContextProperties.SessionId, out var id) ? id : null;

    /// <summary>
    /// Sets the session ID in the operation context.
    /// </summary>
    /// <param name="context">The operation context.</param>
    /// <param name="id">The session ID to set.</param>
    public static void SetSessionId(this OperationContext context, Guid? id)
    {
        if (id.HasValue)
            context.SetValue(OperationServiceContextProperties.SessionId, id.Value);
    }

    /// <summary>
    /// Retrieves the recipient ID from the operation context.
    /// </summary>
    /// <param name="context">The operation context.</param>
    /// <returns>The recipient ID if present; otherwise, null.</returns>
    public static string? GetRecipientId(this OperationContext context) => context.TryGetValue<string>(OperationServiceContextProperties.RecipientId, out var id) ? id : null;

    /// <summary>
    /// Sets the recipient ID in the operation context.
    /// </summary>
    /// <param name="context">The operation context.</param>
    /// <param name="id">The recipient ID to set.</param>
    public static void SetRecipientId(this OperationContext context, string? id)
    {
        if (!string.IsNullOrEmpty(id))
            context.SetValue(OperationServiceContextProperties.RecipientId, id);
    }

    /// <summary>
    /// Retrieves the command handler instance from the operation context.
    /// </summary>
    /// <typeparam name="T">The type of command handler to retrieve.</typeparam>
    /// <param name="context">The operation context.</param>
    /// <returns>The command handler instance if present; otherwise, null.</returns>
    public static T? GetCommandHandler<T>(this OperationContext context) => context.TryGetValue<T>(OperationServiceContextProperties.CommandHandler, out var handler) ? handler : default(T);

    /// <summary>
    /// Sets the command handler instance in the operation context.
    /// </summary>
    /// <param name="context">The operation context.</param>
    /// <param name="handler">The command handler instance to set.</param>
    public static void SetCommandHandler(this OperationContext context, object? handler)
    {
        if (handler != null)
            context.SetValue(OperationServiceContextProperties.CommandHandler, handler);
    }

    /// <summary>
    /// Retrieves the command ID from the operation context.
    /// </summary>
    /// <param name="context">The operation context.</param>
    /// <returns>The command ID if present; otherwise, null.</returns>
    public static Guid? GetCommandId(this OperationContext context) => context.TryGetValue<Guid>(OperationServiceContextProperties.CommandId, out var id) ? id : null;

    /// <summary>
    /// Sets the command object in the operation context.
    /// </summary>
    /// <param name="context">The operation context.</param>
    /// <param name="cmd">The command object to set.</param>
    public static void SetCommand(this OperationContext context, object cmd)
    {
        if (cmd != null)
            context.SetValue(OperationServiceContextProperties.Command, cmd);
    }

    /// <summary>
    /// Sets the command ID in the operation context.
    /// </summary>
    /// <param name="context">The operation context.</param>
    /// <param name="id">The command ID to set.</param>
    public static void SetCommandId(this OperationContext context, Guid? id)
    {
        if (id.HasValue)
            context.SetValue(OperationServiceContextProperties.CommandId, id.Value);
    }

    /// <summary>
    /// Retrieves the command name from the operation context.
    /// </summary>
    /// <param name="context">The operation context.</param>
    /// <returns>The command name if present; otherwise, null.</returns>
    public static string? GetCommandName(this OperationContext context) => context.TryGetValue<string>(OperationServiceContextProperties.CommandName, out var name) ? name : null;

    /// <summary>
    /// Retrieves the command object from the operation context.
    /// </summary>
    /// <typeparam name="T">The type of command to retrieve.</typeparam>
    /// <param name="context">The operation context.</param>
    /// <returns>The command object if present; otherwise, null.</returns>
    public static T? GetCommand<T>(this OperationContext context) => context.TryGetValue<T>(OperationServiceContextProperties.Command, out var obj) ? obj : default;

    /// <summary>
    /// Sets the command name in the operation context.
    /// </summary>
    /// <param name="context">The operation context.</param>
    /// <param name="name">The command name to set.</param>
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

/// <summary>
/// Base class for command handler executors providing common execution logic.
/// </summary>
/// <typeparam name="THandler">The type of command handler.</typeparam>
abstract class CommandHandlerExecutorBase<THandler>(PlumberEngine plumber, ILogger log)
    : ICommandHandleExecutor<THandler>
    where THandler : ICommandHandler, IServiceTypeRegister
{
    private readonly IServicesConvention _serviceConventions = plumber.Config.Conventions.ServicesConventions();

    /// <inheritdoc/>
    public abstract Task Handle<TCommand>(Metadata m, TCommand command);

    /// <summary>
    /// Handles command execution, including error handling and result event generation.
    /// </summary>
    /// <typeparam name="TCommand">The type of command.</typeparam>
    /// <param name="ch">The command handler instance.</param>
    /// <param name="m">The metadata.</param>
    /// <param name="command">The command to execute.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected async Task OnHandle<TCommand>(ICommandHandler<TCommand> ch, Metadata m, TCommand command)
    {
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

            await plumber.AppendEventToStream(operationContext, cmdStream, evt, StreamState.Any, evtName);
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
            await plumber.AppendEventToStream(operationContext, cmdStream, evt, StreamState.Any, evtName);
        }
        catch (FaultException ex)
        {
            var faultData = ex.GetFaultData();
            var evt = CommandFailed.Create(cmdId, ex.Message, sw.Elapsed, (HttpStatusCode)ex.Code, faultData);
            var evtName = $"{cmdName}Failed<{faultData.GetType().Name}>";
            await plumber.AppendEventToStream(operationContext, cmdStream, evt, StreamState.Any, evtName);
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
            await plumber.AppendEventToStream(operationContext, cmdStream, evt, StreamState.Any, evtName);
            log.LogDebug(ex, "Command {CommandType}Failed appended to session steam {CommandStream}.", command.GetType().Name,
                cmdStream);
        }
    }

}

/// <summary>
/// Executes command handlers within a scoped service provider.
/// </summary>
/// <typeparam name="THandler">The type of command handler.</typeparam>
class CommandHandlerScopedExecutor<THandler>(
    PlumberEngine plumber,
    ILogger<CommandHandlerScopedExecutor<THandler>> log) : CommandHandlerExecutorBase<THandler>(plumber, log)
    where THandler : ICommandHandler, IServiceTypeRegister
{
    /// <inheritdoc/>
    public override async Task Handle<TCommand>(Metadata m, TCommand command)
    {

        await using var scope = plumber.Config.ServiceProvider.CreateAsyncScope();
        var ch = (ICommandHandler<TCommand>)scope.ServiceProvider.GetRequiredService(typeof(ICommandHandler<TCommand>));

        await OnHandle(ch, m, command);
    }

}

/// <summary>
/// Executes command handlers using singleton handler instances.
/// </summary>
/// <typeparam name="THandler">The type of command handler.</typeparam>
class CommandHandlerSingletonExecutor<THandler>(
        PlumberEngine plumber,
        ILogger<CommandHandlerSingletonExecutor<THandler>> log)
        : CommandHandlerExecutorBase<THandler>(plumber, log)
        where THandler : ICommandHandler, IServiceTypeRegister
    {
        private readonly IServicesConvention _serviceConventions = plumber.Config.Conventions.ServicesConventions();
        private readonly ConcurrentDictionary<Type, ICommandHandler> _invokers = new();

        /// <inheritdoc/>
        public override async Task Handle<TCommand>(Metadata m, TCommand command)
        {
            var ch = (ICommandHandler<TCommand>)_invokers.GetOrAdd(typeof(TCommand),
                x => plumber.Config.ServiceProvider.GetRequiredService<ICommandHandler<TCommand>>());

            await base.OnHandle(ch, m, command);

        }


    
}