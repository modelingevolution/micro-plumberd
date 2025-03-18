using System;
using System.Collections.Concurrent;
using EventStore.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MicroPlumberd.Services.ProcessManagers;

public class ProcessManagerExecutor<TProcessManager>(ProcessManagerClient pmClient, ILogger<ProcessManagerExecutor<TProcessManager>> log)  : IEventHandler, ITypeRegister
    where TProcessManager : IProcessManager, ITypeRegister
{
    public class Lookup : IEventHandler, ITypeRegister
    {
        private readonly Dictionary<Guid, Guid> _managerByReceiverId = new Dictionary<Guid, Guid>();
        public Guid? GetProcessManagerIdByReceiverId(Guid receiverId) => _managerByReceiverId.TryGetValue(receiverId, out var v) ? v : null;

        private void Given(Metadata m, ICommandEnqueued ev)
        {
            _managerByReceiverId[ev.RecipientId] = m.Id;
        }

        public async Task Handle(Metadata m, object ev)
        {
            if (ev is ICommandEnqueued sf)
                Given(m, sf);
        }
        
        public static IEnumerable<Type> Types=> TProcessManager.CommandTypes;
    }
    internal class Sender(IProcessManagerClient pmClient) : IEventHandler, ITypeRegister
    {
        public async Task Handle(Metadata m, object cmd)
        {
            var c = (ICommandEnqueued)cmd;
            try
            {
                await pmClient.Bus.SendAsync(c.RecipientId, c.Command);
            }
            catch (Exception ex)
            {
                var manager = await pmClient.GetManager<TProcessManager>(c.RecipientId);

                CommandInvocationFailed evt = new CommandInvocationFailed() { Command = CommandRequest.Create(c.RecipientId, c.Command), Message = ex.Message, RecipientId = c.RecipientId };
                var plm = pmClient.Plumber;
                var streamId = plm.Config.Conventions.GetStreamIdConvention(null, typeof(TProcessManager), manager.Id);
                await plm.AppendEvents(streamId, StreamState.Any, evt);

                Guid causationId = m.CausationId() ?? throw new InvalidOperationException("Causation id is not provided.");
                var causationEvent = await plm.FindEventInStream(streamId, causationId, pmClient.Plumber.TypeHandlerRegisters.GetEventNameConverterFor<TProcessManager>());

                ExecutionContext context = new ExecutionContext(causationEvent.Metadata, causationEvent.Event, c.RecipientId, CommandRequest.Create(c.RecipientId,c.Command),ex);
                var compensationCommand = await manager.HandleError(context);
                if (compensationCommand != null)
                {
                    var evt2 =  CommandEnqueued.Create(compensationCommand.RecipientId, compensationCommand.Command);
                    await plm.AppendEvents(streamId, StreamState.StreamExists, evt2);
                }
            }

        }

        public static IEnumerable<Type> Types => TProcessManager.CommandTypes;
    }

    
    public async Task Handle(Metadata m, object evt)
    {
        var plb = pmClient.Plumber;
        IProcessAction? action = null;
        string streamId = string.Empty;
        if (TProcessManager.StartEvent == evt.GetType())
        {
            Guid aggId = Guid.Parse(m.SourceStreamId.Substring(m.SourceStreamId.IndexOf('-')+1));
            var manager = await pmClient.GetManager<TProcessManager>(aggId);
            streamId = plb.Config.Conventions.GetStreamIdConvention(null,typeof(TProcessManager), manager.Id);
            action = await manager.StartWhen(m, evt);
        }
        else
        {
            var manager = await pmClient.GetManager<TProcessManager>(m.Id);
            streamId = plb.Config.Conventions.GetStreamIdConvention(null, typeof(TProcessManager), manager.Id);
            if (manager.Version < 0)
            {
                log.LogDebug("We've received event to process-manager that was not created.");
                return;
            }
            action = await manager.When(m, evt);
        }

        await plb.AppendLink(streamId,m);
        if (action is ICommandRequest cmd) 
            await plb.AppendEvents(streamId, StreamState.Any, CommandEnqueued.Create(cmd.RecipientId, cmd.Command));
        else if (action is IStateChangeAction s) 
            await plb.AppendEvents(streamId, StreamRevision.FromInt64(s.Version), s.Events);
    }
    
   
    public static IEnumerable<Type> Types => TProcessManager.Types;
}