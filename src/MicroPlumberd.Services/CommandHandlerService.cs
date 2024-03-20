using EventStore.Client;
using MicroPlumberd.DirectConnect;
using Microsoft.Extensions.Hosting;

namespace MicroPlumberd.Services;

sealed class CommandHandlerService(IPlumber plumber, IEnumerable<ICommandHandlerStarter> starters) : BackgroundService, IEventHandler, IAsyncDisposable
{
    private readonly Dictionary<Type, IEventHandler> _handlersByCommand = new();
    private IAsyncDisposable? _subscription;

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

        var outputStream = plumber.Config.Conventions.ServicesConventions().AppCommandStreamConvention();
        
        this._subscription = await plumber.SubscribeEventHandle(eventMapper.TryGetValue, events, this, outputStream, FromStream.End, true);
        
    }

    public async Task Handle(Metadata m, object ev)
    {
        if (_handlersByCommand.TryGetValue(ev.GetType(), out var executor))
        {
            var tmp = InvocationContext.Current.Clone();
            await Task.Run(async () =>
            {
                using var scope = new InvocationScope(tmp);
                await executor.Handle(m, ev);
            });
        }
    }

    private async ValueTask DisposeAsyncCore()
    {
        if(_subscription != null)
            await _subscription.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore();
        GC.SuppressFinalize(this);
    }
}