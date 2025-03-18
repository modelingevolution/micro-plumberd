using System.Diagnostics;
using System.Text;
using EventStore.Client;
using JetBrains.Annotations;
using MicroPlumberd.Utils;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MicroPlumberd.Services;

sealed class CommandHandlerService(ILogger<CommandHandlerService> log, 
    PlumberEngine plumber, 
    IEnumerable<ICommandHandlerStarter> starters) : BackgroundService, IEventHandler
{
    private readonly Dictionary<Type, IEventHandler> _handlersByCommand = new();
    private IAsyncDisposable? _subscription;
    private Dictionary<string, Type> _eventMapper;
    public bool IsReady { get; private set; }
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_subscription != null)
            await _subscription.DisposeAsync();
        _subscription = null;
    }

    

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _handlersByCommand.Clear();
            foreach (var i in starters)
            {
                var executor = CommandHandlerExecutor.CreateScoped(plumber, i.HandlerType);
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
                    FromStream.End, true, stoppingToken);
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
            var tmp =  OperationContext.Current;
            _ = Task.Factory.StartNew(async () =>
            {
                OperationContext.ClearContext();
                Debug.Assert(OperationContext.Current == null);
                var context = OperationContext.Create(Flow.CommandHandler);
                using var scope = context.CreateScope();

                Debug.Assert(OperationContext.Current != null);
                if (IdDuckTyping.Instance.TryGetGuidId(ev, out var id))
                    context.SetCausationId(id);

                context.SetCorrelationId(m.CorrelationId());
                context.SetUserId(m.UserId());
                while (true)
                    try
                    {
                        await executor.Handle(m, ev);
                        break;
                    }
                    
                    catch (Exception ex) // this won't be executed because of business exception, CommandExecutor have try/catch.
                                         // This will be called because there is a problem with parsing arguments.
                    {
                        var decision = await plumber.Config.HandleError(ex, context, default);
                        switch (decision)
                        {
                            case ErrorHandleDecision.Retry:
                                continue;
                            case ErrorHandleDecision.Cancel:
                                throw new OperationCanceledException("Operation canceled by user.");
                            case ErrorHandleDecision.Ignore:
                                return;
                        }
                    }
            });
            Debug.Assert(OperationContext.Current != null);
            Debug.Assert(OperationContext.Current == tmp);
        }
    }

    //public async Task Handle(Metadata m, object ev)
    //{
    //    if (_handlersByCommand.TryGetValue(ev.GetType(), out var executor))
    //    {
    //        var tmp = InvocationContext.Current.Clone();
    //        _ = Task.Run(async () =>
    //        {
    //            using var scope = new InvocationScope(tmp);
    //            if(IdDuckTyping.Instance.TryGetGuidId(ev, out var id))
    //                scope.SetCausation(id);
    //            scope.SetUserId(m.UserId());
    //            await executor.Handle(m, ev);
    //        });
    //    } 
    //}


}