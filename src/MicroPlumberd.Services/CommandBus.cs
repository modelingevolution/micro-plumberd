using System.Runtime.CompilerServices;



using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Security.Cryptography.X509Certificates;
using EventStore.Client;
using MicroPlumberd;
using MicroPlumberd.Api;
using MicroPlumberd.Collections;
using MicroPlumberd.Services;

using MicroPlumberd.Utils;
using Microsoft.Extensions.Logging;

namespace MicroPlumberd.Services;

/// <summary>
/// Provides command bus functionality for sending commands and receiving execution results via EventStore streams.
/// </summary>
internal class CommandBus : ICommandBus, IEventHandler
{
    private readonly IPlumberApi _plumber;
    private readonly ICommandBusPool _pool;
    private readonly ILogger<CommandBus> _log;
    private readonly string _streamIn;
    private readonly string _streamOut;
    private readonly ConcurrentDictionary<Guid, CommandExecutionResults> _handlers = new();
    private readonly ConcurrentDictionary<string, Type> _commandMapping = new();
    private readonly ConcurrentHashSet<Type> _supportedCommands = new();
    private AsyncLazy<bool> _initialized;
    private readonly object _sync = new object();
    private IAsyncDisposable? _subscription;
    /// <summary>
    /// Gets the unique session ID for this command bus instance.
    /// </summary>
    public Guid SessionId { get; } = Guid.NewGuid();
    /// <summary>
    /// Initializes a new instance of the <see cref="CommandBus"/> class.
    /// </summary>
    /// <param name="plumber">The plumber API instance.</param>
    /// <param name="pool">The command bus pool that owns this instance.</param>
    /// <param name="log">The logger instance.</param>
    public CommandBus(IPlumberApi plumber, ICommandBusPool pool, ILogger<CommandBus> log)
    {
        _plumber = plumber;
        _pool = pool;
        _log = log;
        var servicesConventions = plumber.Config.Conventions.ServicesConventions();
        _streamIn = servicesConventions.SessionInStreamFromSessionIdConvention(SessionId);
        _streamOut = servicesConventions.SessionOutStreamFromSessionIdConvention(SessionId);
        _initialized = new AsyncLazy<bool>(OnInitialize);
    }

    /// <summary>
    /// Initializes the command bus by setting up stream metadata and subscriptions.
    /// </summary>
    /// <returns>A task representing the asynchronous initialization, with a result of true when complete.</returns>
    private async Task<bool> OnInitialize()
    {
        await _plumber.Client.SetStreamMetadataAsync(_streamIn, StreamState.NoStream, new EventStore.Client.StreamMetadata(maxAge: TimeSpan.FromDays(30)));
        await _plumber.Client.SetStreamMetadataAsync(_streamOut, StreamState.NoStream, new EventStore.Client.StreamMetadata(maxAge: TimeSpan.FromDays(30)));
        _subscription = await _plumber.SubscribeEventHandler(TryMapEventResponse, null, this, _streamOut, FromStream.End, false);
        _log.LogDebug("Session {steamId} subscribed.", _streamOut);
       
        return true;
    }

    /// <summary>
    /// Attempts to map an event response type name to its corresponding CLR type.
    /// </summary>
    /// <param name="type">The event type name.</param>
    /// <param name="t">When this method returns, contains the CLR type if mapping succeeded; otherwise, null.</param>
    /// <returns>True if the mapping succeeded; otherwise, false.</returns>
    private bool TryMapEventResponse(string type, out Type t)
    {
        if (_commandMapping.TryGetValue(type, out t)) return true;

        string supportedMessages = string.Join("\r\n-> ", _commandMapping.Keys);
        string helpMsg;
        int index = type.IndexOf("Failed<");
        if (index != -1)
        {
            string arg = type.Substring(index + 7);
            helpMsg = $"\r\nHave you forgotten to decorate command with [ThrowsFaultException<{arg}] attribute??";
        }
        else
            helpMsg = string.Empty;
        _log.LogWarning("Received unrecognized message type: {type}; Supported message types:{supportedMessages}"+ helpMsg, type, supportedMessages);
        return false;

    }

    private readonly IdDuckTyping _idTyping = new();

    /// <inheritdoc/>
    public async Task QueueAsync(object recipientId, object command, TimeSpan? timeout = null, bool fireAndForget = true, CancellationToken token = default)
    {
        using var scope = await _pool.RentScope(token);
        await scope.SendAsync(recipientId, command, timeout ?? TimeSpan.FromDays(7), fireAndForget, token);
    }
    /// <inheritdoc/>
    public async Task SendAsync(object recipientId, object command, TimeSpan? timeout = null, bool fireAndForget = false, CancellationToken token = default)
    {
        var commandId = GetCommandId(command);
        using var context = OperationContext.GetOrCreate(Flow.Component);

        var causationId = context.Context.GetCausationId() ?? commandId;
        var userId = context.Context.GetUserId();
        var correlationId = context.Context.GetCorrelationId() ?? commandId;
        var retById = IdDuckTyping.Instance.TryGetGuidId(command, out var id) ? id : correlationId;

        var metadata = new
        {
            CorrelationId =  (Guid)correlationId!, 
            CausationId = (Guid)causationId!,
            RecipientId = recipientId.ToString(),
            SessionId = SessionId,
            UserId = userId
        };

        var executionResults = new CommandExecutionResults();
        if (!_handlers.TryAdd(retById, executionResults))
            throw new InvalidOperationException("This command is being executed.");

        //Debug.WriteLine($"===> {SessionId} expects results from command id: {retById}, Context causation is: {InvocationContext.Current.CausactionId()}");
        
        CheckMapping(command);
        await _initialized.Value;

        await _plumber.AppendEvents(_streamIn, StreamState.Any, [command], metadata, token);

        if (fireAndForget)
            return;
        
        bool receivedReturn = await executionResults.IsReady.Task.WaitAsync(timeout ?? _plumber.Config.ServicesConfig().DefaultTimeout, token);
        if (!executionResults.IsSuccess)
        {
            if (!receivedReturn)
            {
                _handlers.TryRemove(metadata.CausationId, out var v);
                throw new TimeoutException("Command execution timeout.");
            }
            else if (executionResults.ErrorData != null)
                throw FaultException.Create(executionResults.ErrorMessage, executionResults.ErrorData, (int)executionResults.ErrorCode);
            throw new FaultException(executionResults.ErrorMessage);
        }
    }

    /// <summary>
    /// Extracts or generates a command ID from the command object.
    /// </summary>
    /// <param name="command">The command object.</param>
    /// <returns>The command ID.</returns>
    private Guid GetCommandId(object command)
    {
        Guid commandId = Guid.NewGuid();
        if (command is IId iid)
        {
            commandId = iid.Uuid;
        }
        else
        {
            var tmpId = _idTyping.GetId(command);
            if (tmpId is Guid g)
            {
                commandId = g;
            }
            else commandId = Guid.NewGuid();
        }

        return commandId;
    }

    /// <summary>
    /// Checks and registers command type mappings for result events.
    /// </summary>
    /// <param name="command">The command object.</param>
    private void CheckMapping(object command)
    {
        var cmdType = command.GetType();
        if (_supportedCommands.Contains(cmdType)) return;
        if (!_supportedCommands.Add(cmdType)) return;

        foreach (var (name, type) in _plumber.Config.Conventions.ServicesConventions().CommandMessageTypes(cmdType))
            _commandMapping.TryAdd(name, type);
    }

    /// <inheritdoc/>
    async Task IEventHandler.Handle(Metadata m, object ev)
    {
        var causationId = ev is ICommandSource cs ? cs.CommandId : (m.CausationId() ?? Guid.Empty);
        if (_handlers.TryGetValue(causationId, out var results))
        {
            if (await results.Handle(m, ev))
            {
                _handlers.TryRemove(causationId, out var x);
                _log.LogDebug("Command execution confirmed: {CommandType}", ev.GetType().GetFriendlyName());
            }
        } 
        else 
            _log.LogDebug("Session event unhandled. CausationId: {CausationId}, SessionId: {SessionId}, Stream: {StreamId}", causationId, SessionId, _streamOut);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => _subscription?.DisposeAsync() ?? ValueTask.CompletedTask;
    /// <inheritdoc/>
    public void Dispose() => _ = DisposeAsync();
}

