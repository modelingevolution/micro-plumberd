using System.ComponentModel;
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
        
            
        var ret = await plumber.AppendEvents(streamId, StreamState.Any, [command], metadata);

        ICommandResult executionResults = (ICommandResult)Activator.CreateInstance(typeof(CommandExecutionResults<>).MakeGenericType(command.GetType()), plumber, ret,command);
        //var start = StreamPosition.FromStreamRevision(ret.NextExpectedStreamRevision);
        var fromStream = FromStream.Start;
        await plumber.SubscribeEventHandle(executionResults.Map, null, executionResults, streamId, fromStream, false);
        executionResults.Wait.WaitOne(TimeSpan.FromSeconds(120));
        if (!executionResults.IsSuccess)
            throw new Exception(executionResults.Error ?? "Timeout");

    }
}

interface ICommandResult : IEventHandler
{
    bool Map(string type, out Type t);
    bool IsSuccess { get; }
    string Error { get; }
    ManualResetEvent Wait { get; }
}
class CommandExecutionResults<TCommand>(IPlumber plumber, IWriteResult results, TCommand source) : ICommandResult
{
    public async Task Handle(Metadata m, object ev)
    {
        if (ev is CommandExecuted ce)
        {
            if (ce.CommandId == m.CausationId())
            {
                IsSuccess = true;
                Wait.Set();
            }
        } else if (ev is CommandFailed cf)
        {
            if (cf.CommandId == m.CausationId())
            {
                IsSuccess = false;
                Error = cf.Message;
                Wait.Set();
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
        }
        t = null;
        return false;
    }

    public bool IsSuccess { get; set; }
    public string Error { get; set; }

    public ManualResetEvent Wait { get; } = new ManualResetEvent(false);
}