using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.Serialization;
using System.Security.Cryptography.X509Certificates;
using EventStore.Client;
using MicroPlumberd;
using MicroPlumberd.Collections;
using MicroPlumberd.Services;
using Microsoft.Extensions.Logging;

namespace MicroPlumberd.Services;

class CommandBus : ICommandBus, IEventHandler
{
    private readonly IPlumber _plumber;
    private readonly ILogger<CommandBus> _log;
    private readonly string _streamIn;
    private readonly string _streamOut;
    private readonly ConcurrentDictionary<Guid, CommandExecutionResults> _handlers = new();
    private readonly ConcurrentDictionary<string, Type> _commandMapping = new();
    private readonly ConcurrentHashSet<Type> _supportedCommands = new();
    private bool _initialized;
    private readonly object _sync = new object();

    public Guid SessionId { get; } = Guid.NewGuid();
    public CommandBus(IPlumber plumber, ILogger<CommandBus> log)
    {
        _plumber = plumber;
        _log = log;
        var servicesConventions = plumber.Config.Conventions.ServicesConventions();
        _streamIn = servicesConventions.SessionInStreamFromSessionIdConvention(SessionId);
        _streamOut = servicesConventions.SessionOutStreamFromSessionIdConvention(SessionId);
    }
    
    

    private async ValueTask CheckInitialized()
    {
        bool shouldSubscribe = false;
        if (!_initialized)
            lock (_sync)
                if (!_initialized)
                {
                    shouldSubscribe = true;
                    _initialized = true;
                }

        if (shouldSubscribe)
        {
            await _plumber.SubscribeEventHandler(TryMapEventResponse, null, this, _streamOut, FromStream.End, false);
            _log.LogDebug("Session {steamId} subscribed.", _streamOut);
        }
    }

    private bool TryMapEventResponse(string type, out Type t)
    {
        if (!_commandMapping.TryGetValue(type, out t))
        {
            _log.LogWarning("Received unrecognized message type: {type}", type);
            return false;
        }

        return true;
    }
    
    
    public async Task SendAsync(Guid recipientId, object command)
    {
        var causationId = InvocationContext.Current.CausactionId();
        var correlationId = InvocationContext.Current.CorrelationId();
        
        var metadata = new
        {
            CorrelationId = (command is IId id && correlationId == null) ? id.Id : correlationId, 
            CausationId = ((command is IId id2 && causationId == null) ? id2.Id : causationId) ?? Guid.NewGuid(),
            RecipientId = recipientId,
            SessionId = SessionId,
        };

        var executionResults = new CommandExecutionResults();
        if (!_handlers.TryAdd(metadata.CausationId, executionResults))
            throw new InvalidOperationException("This command is being executed.");

        CheckMapping(command);
        await CheckInitialized();
        
        await _plumber.AppendEvents(_streamIn, StreamState.Any, [command], metadata);

        bool receivedReturn = await executionResults.IsReady.Task.WaitAsync(_plumber.Config.ServicesConfig().DefaultTimeout);
        
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
        var causationId = m.CausationId() ?? Guid.Empty;
        if (_handlers.TryGetValue(causationId, out var results))
        {
            if (await results.Handle(m, ev))
            {
                _handlers.TryRemove(causationId, out var x);
                _log.LogDebug("Command execution confirmed: {CommandType}", ev.GetType().GetFriendlyName());
            }
        } else _log.LogDebug("Session event unhandled. CausationId: {CausationId}", causationId);
    }
}


[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class ThrowsFaultExceptionAttribute<TMessage>() : ThrowsFaultExceptionAttribute(typeof(TMessage));

public abstract class ThrowsFaultExceptionAttribute(Type thrownType) : Attribute
{
    public Type ThrownType { get; init; } = thrownType;
}


public class CommandExecutionResults 
{
    public async ValueTask<bool> Handle(Metadata m, object ev)
    {
        switch (ev)
        {
            case CommandExecuted ce:
            {
                if (ce.CommandId == m.CausationId())
                {
                    IsSuccess = true;
                    IsReady.SetResult(true);
                    return true;
                }

                break;
            }
            case ICommandFailedEx ef:
            {
                if (ef.CommandId == m.CausationId())
                {
                    IsSuccess = false;
                    ErrorMessage = ef.Message;
                    ErrorData = ef.Fault;
                    ErrorCode = ef.Code;
                        IsReady.SetResult(true);
                        return true;
                }

                break;
            }
            case ICommandFailed cf:
            {
                if (cf.CommandId == m.CausationId())
                {
                    IsSuccess = false;
                    ErrorMessage = cf.Message;
                    ErrorCode = cf.Code;
                    IsReady.SetResult(true);
                    return true;
                }

                break;
            }
        }

        return false;
    }

    public HttpStatusCode ErrorCode { get; private set; }


    public string ErrorMessage { get; private set; }
    public object? ErrorData { get; private set; }
    public bool IsSuccess { get; private set; }
    public TaskCompletionSource<bool> IsReady { get; private set; } = new TaskCompletionSource<bool>();
    
}