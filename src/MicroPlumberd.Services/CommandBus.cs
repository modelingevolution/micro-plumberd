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

namespace MicroPlumberd.Services;

class CommandBus : ICommandBus, IEventHandler
{
    private readonly IPlumber _plumber;
    private readonly string _steamId;
    private readonly ConcurrentDictionary<Guid, CommandExecutionResults> _handlers = new();
    private readonly ConcurrentDictionary<string, Type> _commandMapping = new();
    private readonly ConcurrentHashSet<Type> _supportedCommands = new();
    private bool _initialized;
    private object _sync = new object();

    public Guid SessionId { get; } = Guid.NewGuid();
    public CommandBus(IPlumber plumber)
    {
        _plumber = plumber;
        _steamId = plumber.Config.Conventions.ServicesConventions().SessionStreamFromSessionIdConvention(SessionId);
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
            await _plumber.SubscribeEventHandle(TryMapEventResponse, null, this, _steamId, FromStream.End, false);
    }

    private bool TryMapEventResponse(string type, out Type t)
    {
        if (!_commandMapping.TryGetValue(type, out t))
        {
            Debug.WriteLine($"Received unrecognized message type: {type}");
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
        
        await _plumber.AppendEvents(_steamId, StreamState.Any, [command], metadata);
        
        bool receivedReturn = executionResults.Wait.WaitOne(_plumber.Config.ServicesConfig().DefaultTimeout);
        if (!executionResults.IsSuccess)
        {
            if (!receivedReturn)
            {
                _handlers.TryRemove(metadata.CausationId, out var v);
                throw new TimeoutException("Command execution timeout.");
            }
            else if (executionResults.ErrorData != null)
                throw CommandFaultException.Create(executionResults.ErrorMessage, executionResults.ErrorData);
            throw new CommandFaultException(executionResults.ErrorMessage);
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
        var id = m.CausationId() ?? Guid.Empty;
        if (_handlers.TryGetValue(id, out var results))
        {
            if(await results.Handle(m, ev))
                _handlers.TryRemove(id, out var x);
        }
    }
}


[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class ThrowsFaultCommandExceptionAttribute<TMessage>() : ThrowsFaultCommandExceptionAttribute(typeof(TMessage));

public abstract class ThrowsFaultCommandExceptionAttribute(Type thrownType) : Attribute
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
                    Wait.Set();
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
                    Wait.Set();
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
                    Wait.Set();
                    return true;
                }

                break;
            }
        }

        return false;
    }

    

    public bool IsSuccess { get; private set; }
    public string ErrorMessage { get; private set; }
    public object? ErrorData { get; private set; }
    public ManualResetEvent Wait { get; } = new ManualResetEvent(false);
}