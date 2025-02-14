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
using MicroPlumberd.Collections;
using MicroPlumberd.Services;

using MicroPlumberd.Utils;
using Microsoft.Extensions.Logging;

namespace MicroPlumberd.Services;

class CommandBus : ICommandBus, IEventHandler
{
    private readonly IPlumber _plumber;
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
    public Guid SessionId { get; } = Guid.NewGuid();
    public CommandBus(IPlumber plumber, ICommandBusPool pool, ILogger<CommandBus> log)
    {
        _plumber = plumber;
        _pool = pool;
        _log = log;
        var servicesConventions = plumber.Config.Conventions.ServicesConventions();
        _streamIn = servicesConventions.SessionInStreamFromSessionIdConvention(SessionId);
        _streamOut = servicesConventions.SessionOutStreamFromSessionIdConvention(SessionId);
        _initialized = new AsyncLazy<bool>(OnInitialize);
    }

    private async Task<bool> OnInitialize()
    {
        await _plumber.Client.SetStreamMetadataAsync(_streamIn, StreamState.NoStream, new StreamMetadata(maxAge: TimeSpan.FromDays(30)));
        await _plumber.Client.SetStreamMetadataAsync(_streamOut, StreamState.NoStream, new StreamMetadata(maxAge: TimeSpan.FromDays(30)));
        _subscription = await _plumber.SubscribeEventHandler(TryMapEventResponse, null, this, _streamOut, FromStream.End, false);
        _log.LogDebug("Session {steamId} subscribed.", _streamOut);
       
        return true;
    }


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

    public async Task QueueAsync(object recipientId, object command, TimeSpan? timeout = null, bool fireAndForget = true, CancellationToken token = default)
    {
        using var scope = await _pool.RentScope(token);
        await scope.SendAsync(recipientId, command, timeout ?? TimeSpan.FromDays(7), fireAndForget, token);
    }
    public async Task SendAsync(object recipientId, object command, TimeSpan? timeout = null, bool fireAndForget = false, CancellationToken token = default)
    {
        var commandId = GetCommandId(command);
        var causationId = InvocationContext.Current.CausactionId() ?? commandId;
        var correlationId = InvocationContext.Current.CorrelationId() ?? commandId;
        var retById = IdDuckTyping.Instance.TryGetGuidId(command, out var id) ? id : correlationId;

        var metadata = new
        {
            CorrelationId =  (Guid)correlationId!, 
            CausationId = (Guid)causationId!,
            RecipientId = recipientId.ToString(),
            SessionId = SessionId,
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

    private void CheckMapping(object command)
    {
        var cmdType = command.GetType();
        if (_supportedCommands.Contains(cmdType)) return;
        if (!_supportedCommands.Add(cmdType)) return;

        foreach (var (name, type) in _plumber.Config.Conventions.ServicesConventions().CommandMessageTypes(cmdType))
            _commandMapping.TryAdd(name, type);
    }


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

    public ValueTask DisposeAsync() => _subscription?.DisposeAsync() ?? ValueTask.CompletedTask;
    public void Dispose() => _ = DisposeAsync();
}

public abstract class ThrowsFaultExceptionAttribute(Type thrownType) : Attribute
{
    public Type ThrownType { get; init; } = thrownType;
}