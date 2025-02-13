using System.Diagnostics;
using System.Text;
using EventStore.Client;
using MicroPlumberd.Utils;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MicroPlumberd.Services;

sealed class CommandHandlerService(ILogger<CommandHandlerService> log, IPlumber plumber, IEnumerable<ICommandHandlerStarter> starters) : BackgroundService, IEventHandler
{
    private readonly Dictionary<Type, IEventHandler> _handlersByCommand = new();
    private IAsyncDisposable? _subscription;
    private Dictionary<string, Type> _eventMapper;
    public bool IsReady { get; private set; }
    
    public override void Dispose()
    {
        if(_subscription != null)
            Task.WaitAll(_subscription.DisposeAsync().AsTask());
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _handlersByCommand.Clear();
            foreach (var i in starters)
            {
                var executor = CommandHandlerExecutor.Create(plumber, i.HandlerType);
                foreach (var c in i.CommandTypes) _handlersByCommand.Add(c, executor);
            }

            if (_handlersByCommand.Count == 0)
            {
                IsReady = true;
                return;
            }
            this._eventMapper = _handlersByCommand.Keys.ToDictionary(x => x.Name);
            var events = _handlersByCommand.Keys.Select(x => x.Name).ToArray();

            var settings = plumber.Config.Conventions.ServicesConventions();
            var outputStream = settings.AppCommandStreamConvention();

            if (settings.AreCommandHandlersExecutedPersistently())
                this._subscription = await plumber.SubscribeEventHandlerPersistently(MapCommandType, events, this,
                    outputStream, AppDomain.CurrentDomain.FriendlyName, StreamPosition.End, true, token: stoppingToken);
            else
                this._subscription = await plumber.SubscribeEventHandler(MapCommandType, events, this, outputStream,
                    FromStream.End, true);
            IsReady = true;
        }
        catch (OperationCanceledException ex)
        {
            // do nothing
        }
        catch(Exception ex)
        {
            throw;
        }
    }

    private bool MapCommandType(string evtType, out Type t)
    {
        Debug.WriteLine($"Handling {evtType} command.");
        if (_eventMapper.TryGetValue(evtType, out t))
        {
            Debug.WriteLine($"Handling command: {evtType}");
            return true;
        }

        log.LogError(new StringBuilder().Append("Found unrecognized command type in app command stream. ")
            .Append(evtType)
            .ToString());

        return false;
    }

    
    public async Task Handle(Metadata m, object ev)
    {
        if (_handlersByCommand.TryGetValue(ev.GetType(), out var executor))
        {
            var tmp = InvocationContext.Current.Clone();
            _ = Task.Run(async () =>
            {
                using var scope = new InvocationScope(tmp);
                if(IdDuckTyping.Instance.TryGetGuidId(ev, out var id))
                    scope.SetCausation(id);
                await executor.Handle(m, ev);
            });
        } 
    }

   
}