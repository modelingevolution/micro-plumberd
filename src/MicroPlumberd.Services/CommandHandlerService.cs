using EventStore.Client;
using MicroPlumberd.DirectConnect;
using Microsoft.Extensions.Hosting;

namespace MicroPlumberd.Services;

class CommandHandlerService(IPlumber plumber, IEnumerable<ICommandHandlerStarter> starters) : BackgroundService, IEventHandler
{
    private readonly Dictionary<Type, IEventHandler> _handlersByCommand = new();
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _handlersByCommand.Clear();
        foreach (var i in starters)
        {
            var executor = CommandHandlerExecutor.Create(plumber, i.HandlerType);
            foreach (var c in i.CommandTypes) _handlersByCommand.Add(c, executor);
        }

        var eventMapper = _handlersByCommand.Keys.ToDictionary(x => x.Name);
        var events = _handlersByCommand.Keys.Select(x => x.Name).ToArray();

        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        var assemblyName = assembly.GetName().Name;
        
        await plumber.SubscribeEventHandle(eventMapper.TryGetValue, events, this, assemblyName, FromStream.End, true);
        
    }

    public async Task Handle(Metadata m, object ev)
    {
        if(_handlersByCommand.TryGetValue(ev.GetType(), out var executor))
            await executor.Handle(m, ev);
    }
}