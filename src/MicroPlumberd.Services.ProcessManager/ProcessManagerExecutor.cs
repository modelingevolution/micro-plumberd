using System.Collections.Concurrent;
using EventStore.Client;
using Microsoft.Extensions.DependencyInjection;

namespace MicroPlumberd.Services.ProcessManagers;

public class ProcessManagerExecutor<TProcessManager>(ProcessManagerClient pmClient)  : IEventHandler, ITypeRegister
    where TProcessManager : IProcessManager, ITypeRegister
{
    public class Lookup : IEventHandler, ITypeRegister
    {
        private readonly Dictionary<Guid, Guid> _managerByReceiverId = new Dictionary<Guid, Guid>();
        public Guid? GetProcessManagerIdByReceiverId(Guid receiverId) => _managerByReceiverId.TryGetValue(receiverId, out var v) ? v : null;

        private void Given(Metadata m, ICommandEnqueued ev)
        {
            _managerByReceiverId[m.Id] = ev.RecipientId;
        }

        public async Task Handle(Metadata m, object ev)
        {
            if (ev is ICommandEnqueued sf)
                Given(m, sf);
        }
        public static IReadOnlyDictionary<string, Type> TypeRegister => TProcessManager.TypeRegister;
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
                await pmClient.Plumber.AppendEvents($"{typeof(TProcessManager).Name}-{manager.Id}", StreamState.Any, evt);

                Guid causationId = m.CausationId() ?? throw new InvalidOperationException("Causation id is not provided.");
                var causationEvent = await pmClient.Plumber
                    .FindEventInStream($"{typeof(TProcessManager).Name}-{manager.Id}", causationId, TProcessManager.TypeRegister.TryGetValue);

                ExecutionContext context = new ExecutionContext(causationEvent.Metadata, causationEvent.Event, c.RecipientId, CommandRequest.Create(c.RecipientId,c.Command),ex);
                var compensationCommand = await manager.HandleError(context);
                if (compensationCommand != null)
                {
                    var evt2 =  CommandEnqueued.Create(compensationCommand.RecipientId, compensationCommand.Command);
                    await pmClient.Plumber.AppendEvents($"{typeof(TProcessManager).Name}-{manager.Id}", StreamState.StreamExists, evt2);
                }
            }

        }

        public static IReadOnlyDictionary<string, Type> TypeRegister => TProcessManager.CommandTypes.ToDictionary(x => x.GetFriendlyName());
    }

    
    public async Task Handle(Metadata m, object evt)
    {
        var manager = await pmClient.GetManager<TProcessManager>(m.Id);
        ICommandRequest? cmd = null;

        if (TProcessManager.StartEvent == evt.GetType())
            cmd = await manager.StartWhen(m, evt);
        else
        {
            if (manager.Version < 0) return;
            cmd = await manager.When(m, evt);
        }

        // TODO: This should be done as a single transaction!
        await pmClient.Plumber.AppendLink($"{typeof(TProcessManager).Name}-{manager.Id}",m);
        if (cmd != null) 
            await pmClient.Plumber.AppendEvents($"{typeof(TProcessManager).Name}-{manager.Id}", StreamState.Any, CommandEnqueued.Create(cmd.RecipientId, cmd.Command));
        
    }


    public static IReadOnlyDictionary<string, Type> TypeRegister => TProcessManager.TypeRegister;
}