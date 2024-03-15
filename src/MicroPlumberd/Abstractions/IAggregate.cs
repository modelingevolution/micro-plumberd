using System;
using System.Diagnostics.CodeAnalysis;
using EventStore.Client;
using Microsoft.Extensions.DependencyInjection;

namespace MicroPlumberd;

public delegate bool TypeEventConverter(string type, out Type t);
public interface ITypeRegister
{
    static abstract IReadOnlyDictionary<string, Type> TypeRegister { get; }
    
}

public interface IAggregate
{
    Guid Id { get; }
    long Version { get; }
    IReadOnlyList<object> PendingEvents { get; }
    Task Rehydrate(IAsyncEnumerable<object> events);
    void AckCommitted();
}


public record ExecutionContext(Metadata Metadata, object Event, Guid Id, CommandRequest? Command, Exception Exception);

public record CommandInvocationFailed
{
    public Guid RecipientId { get; init; }
    public CommandRequest Command { get; init; }
    public string Message { get; init; }
}
public record CommandRequest(Guid RecipientId, object Command);


public interface IProcessManager : IEventHandler
{
    Guid Id { get; set; }
    Task<CommandRequest?> HandleError(ExecutionContext executionContext);
    Task<CommandRequest?> When(Metadata m, object evt);
}

public interface ICommandEnqueued
{
    object Command { get; }
    Guid RecipientId { get; }
}
abstract class CommandEnqueued : ICommandEnqueued
{
    protected object _command;
    object ICommandEnqueued.Command => _command;
    public Guid RecipientId { get; private set; }
    public static CommandEnqueued Create(Guid recipient, object command)
    {
        var type = typeof(CommandEnqueued<>).MakeGenericType(command.GetType());
        var cmd = (CommandEnqueued)Activator.CreateInstance(type);
        cmd.RecipientId = recipient;
        cmd._command = command;
        
        return cmd;
    }
}
sealed class CommandEnqueued<TCommand> : CommandEnqueued
{
    public TCommand Command => (TCommand)_command;
}


public interface ICommandBus
{
    Task SendAsync(Guid recipientId, object command);
}
public class ProcessManagerExecutor<TProcessManager>(ProcessManagerClient pmClient)  : IEventHandler, ITypeRegister
    where TProcessManager : IProcessManager, ITypeRegister
{
    public class Lookup : IEventHandler, ITypeRegister
    {
        private readonly Dictionary<Guid, Guid> _managerByReceiverId = new Dictionary<Guid, Guid>();
        public Guid? GetProcessManagerIdByReceiverId(Guid receiverId) => _managerByReceiverId[receiverId];

        private void Given(Metadata m, CommandEnqueued ev)
        {
            _managerByReceiverId[m.Id] = ev.RecipientId;
        }

        public async Task Handle(Metadata m, object ev)
        {
            if (ev is CommandEnqueued sf)
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

                CommandInvocationFailed evt = new CommandInvocationFailed() { Command = new CommandRequest(c.RecipientId, c.Command), Message = ex.Message, RecipientId = c.RecipientId };
                await pmClient.Plumber.AppendEvents($"{typeof(TProcessManager).Name}-{manager.Id}", StreamState.Any, evt);

                Guid causationId = m.CausationId() ?? throw new InvalidOperationException("Causation id is not provided.");
                var causationEvent = await pmClient.Plumber
                    .FindEventInStream($"{typeof(TProcessManager).Name}-{manager.Id}", causationId, TProcessManager.TypeRegister.TryGetValue);

                ExecutionContext context = new ExecutionContext(causationEvent.Metadata, causationEvent.Event, c.RecipientId, new CommandRequest(c.RecipientId,c.Command),ex);
                var compensationCommand = await manager.HandleError(context);
                if (compensationCommand != null)
                {
                    var evt2 =  CommandEnqueued.Create(compensationCommand.RecipientId, compensationCommand.Command);
                    await pmClient.Plumber.AppendEvents($"{typeof(TProcessManager).Name}-{manager.Id}", StreamState.StreamExists, evt2);
                }
            }

        }

        public static IReadOnlyDictionary<string, Type> TypeRegister => TProcessManager.TypeRegister;
    }

    
    public async Task Handle(Metadata m, object evt)
    {
        var manager = await pmClient.GetManager<TProcessManager>(m.Id);
        CommandRequest? cmd = null;
        
        cmd = await manager.When(m, evt);

        await pmClient.Plumber.AppendLink($"{typeof(TProcessManager).Name}-{manager.Id}",m);
        if (cmd != null) 
            await pmClient.Plumber.AppendEvents($"{typeof(TProcessManager).Name}-{manager.Id}", StreamState.Any, CommandEnqueued.Create(cmd.RecipientId, cmd.Command));
    }


    public static IReadOnlyDictionary<string, Type> TypeRegister => TProcessManager.TypeRegister;
}

public interface IProcessManagerClient
{
    IPlumber Plumber { get; }
    ICommandBus Bus { get; }

    Task<TProcessManager> GetManager<TProcessManager>(Guid commandRecipientId)
        where TProcessManager : IProcessManager, ITypeRegister;
}

public class ProcessManagerClient : IProcessManagerClient
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IPlumber _plumber;

    public ProcessManagerClient(IServiceProvider serviceProvider, IPlumber plumber, ICommandBus bus)
    {
        _serviceProvider = serviceProvider;
        _plumber = plumber;
        Bus = bus;
    }

    public ICommandBus Bus { get; }
    public IPlumber Plumber { get => _plumber; }

    public async Task<IAsyncDisposable> SubscribeProcessManager<TProcessManager>() where TProcessManager:IProcessManager, ITypeRegister
    {
        ProcessManagerExecutor<TProcessManager> executor = new ProcessManagerExecutor<TProcessManager>(this);
        ProcessManagerExecutor<TProcessManager>.Sender sender =
            new ProcessManagerExecutor<TProcessManager>.Sender(this);
        var c = AsyncDisposableCollection.New();
        c += await Plumber.SubscribeEventHandlerPersistently(sender, $"$ct-{typeof(TProcessManager)}Lookup");
        c += await Plumber.SubscribeEventHandlerPersistently(executor, "");
        return c;
    }
    internal T CreateProcessManager<T>()
    {
        if (_serviceProvider != null) return _serviceProvider.GetService<T>() ?? Activator.CreateInstance<T>();
        return Activator.CreateInstance<T>();
    }

    public async Task<TProcessManager> GetManager<TProcessManager>(Guid commandRecipientId) where TProcessManager:IProcessManager, ITypeRegister
    {
        var lookup = new ProcessManagerExecutor<TProcessManager>.Lookup();

        // This stream is created straight from 
        await _plumber.Rehydrate(lookup, $"{typeof(TProcessManager)}Lookup-{commandRecipientId}");

        var managerId = lookup.GetProcessManagerIdByReceiverId(commandRecipientId) ?? Guid.NewGuid();
        var manager = CreateProcessManager<TProcessManager>();
        manager.Id = managerId;

        await _plumber.Rehydrate(manager, $"{typeof(TProcessManager).Name}-{managerId}");

        return manager;
    }
}

class AsyncDisposableCollection : IAsyncDisposable
{
    private readonly List<IAsyncDisposable> _items = new();
    public static AsyncDisposableCollection New() => new AsyncDisposableCollection();
    public static AsyncDisposableCollection operator+(AsyncDisposableCollection left, IAsyncDisposable right)
    {
        left._items.Add(right);
        return left;
    }
    
    public async ValueTask DisposeAsync()
    {
        foreach (var i in _items)
            await i.DisposeAsync();
    }
    
}





public interface IAggregate<out TSelf> : IAggregate
{
    static abstract TSelf New(Guid id);
   
}