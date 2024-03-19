using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Security.Cryptography.X509Certificates;
using EventStore.Client;
using MicroPlumberd;
using MicroPlumberd.Services;

namespace MicroPlumberd.Services;

class CommandBus(IPlumber plumber) : ICommandBus
{
    public async Task SendAsync(Guid recipientId, object command)
    {
        var causationId = InvocationContext.Current.CausactionId();
        var correlationId = InvocationContext.Current.CorrelationId();

        var streamId = plumber.Config.Conventions.GetSteamIdFromCommand(command.GetType(), recipientId);
        var metadata = new
        {
            CorrelationId = (command is IId id && correlationId == null) ? id.Id : correlationId, 
            CausationId = (command is IId id2 && causationId == null) ? id2.Id : causationId,
            RecipientId = recipientId
        };

        ICommandResult? executionResults = (ICommandResult)Activator.CreateInstance(typeof(CommandExecutionResults<>).MakeGenericType(command.GetType()))!;
        await plumber.SubscribeEventHandle(executionResults.Map, null, executionResults, streamId, FromStream.End, false);
        await plumber.AppendEvents(streamId, StreamState.Any, [command], metadata);
        
        bool receivedReturn = executionResults.Wait.WaitOne(TimeSpan.FromSeconds(120));
        if (!executionResults.IsSuccess)
        {
            if (!receivedReturn)
                throw new TimeoutException("Command execution timeout.");
            else if (executionResults.ErrorData != null)
                throw CommandFaultException.Create(executionResults.ErrorMessage, executionResults.ErrorData);
            throw new CommandFaultException(executionResults.ErrorMessage);
        }
    }
}



interface ICommandResult : IEventHandler
{
    bool Map(string type, out Type t);
    bool IsSuccess { get; }
    string ErrorMessage { get; }
    object? ErrorData { get; }
    ManualResetEvent Wait { get; }
}
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class ThrowsFaultCommandExceptionAttribute<TMessage>() : ThrowsFaultCommandExceptionAttribute(typeof(TMessage));

public abstract class ThrowsFaultCommandExceptionAttribute(Type thrownType) : Attribute
{
    public Type ThrownType { get; init; } = thrownType;
}
public class CommandExecutionResults<TCommand>() : ICommandResult
{
    private static readonly IDictionary<string, Type> _exceptionMap= typeof(TCommand)
        .GetCustomAttributes()
        .Where(x => x is ThrowsFaultCommandExceptionAttribute)
        .OfType<ThrowsFaultCommandExceptionAttribute>()
        .Select(x => x.ThrownType)
        .ToDictionary(x => $"{typeof(TCommand).Name}Failed<{x.Name}>", 
            x => typeof(CommandExecuted<>).MakeGenericType(x));
    
    public async Task Handle(Metadata m, object ev)
    {
        switch (ev)
        {
            case CommandExecuted ce:
            {
                if (ce.CommandId == m.CausationId())
                {
                    IsSuccess = true;
                    Wait.Set();
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
                }

                break;
            }
        }
    }

    public bool Map(string type, out Type t)
    {
        var cmdType = typeof(TCommand).Name;
        if (type == cmdType)
        {
            t = typeof(TCommand);
            return true;
        } else if (type == $"{cmdType}Executed")
        {
            t = typeof(CommandExecuted);
            return true;
        } else if (type == $"{cmdType}Failed")
        {
            t = typeof(CommandFailed);
            return true;
        } else if (_exceptionMap.TryGetValue(type, out t))
        {
            return true;
        } else if (type.StartsWith($"{cmdType}Failed"))
        {
            // most likely someone forgotten to put attribute on a command.
            t = typeof(CommandFailed);
            return true;
        }
        t = null;
        return false;
    }

    public bool IsSuccess { get; private set; }
    public string ErrorMessage { get; private set; }
    public object? ErrorData { get; private set; }
    public ManualResetEvent Wait { get; } = new ManualResetEvent(false);
}