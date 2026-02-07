using MicroPlumberd;
using MicroPlumberd.Services;
using ModelingEvolution.EventAggregator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MicroPlumberd.Services.EventAggregator;

/// <summary>
/// Starts an event handler that receives events from EventAggregator instead of EventStore.
/// Creates a single scoped container for the handler and subscribes to all event types.
/// </summary>
class EventAggregatorEventHandlerStarter<THandler, TId> : IEventHandlerStarter
    where THandler : class, IEventHandler, ITypeRegister
    where TId : IParsable<TId>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EventAggregatorEventHandlerStarter<THandler, TId>> _logger;
    private readonly PlumberEngine _plumber;
    private IServiceScope? _scope;
    private readonly List<IDisposable> _subscriptions = new();

    public EventAggregatorEventHandlerStarter(
        IServiceProvider serviceProvider,
        PlumberEngine plumber,
        ILogger<EventAggregatorEventHandlerStarter<THandler, TId>> logger)
    {
        _serviceProvider = serviceProvider;
        _plumber = plumber;
        _logger = logger;
    }

    public Task Start(CancellationToken stoppingToken)
    {
        _scope = _serviceProvider.CreateScope();
        var handler = _scope.ServiceProvider.GetRequiredService<THandler>();
        var eventAggregator = _scope.ServiceProvider.GetRequiredService<IEventAggregator>();
        var context = _scope.ServiceProvider.GetRequiredService<OperationContext>();

        foreach (var eventType in THandler.Types)
        {
            var subscribeMethod = typeof(EventAggregatorEventHandlerStarter<THandler, TId>)
                .GetMethod(nameof(SubscribeToEnvelope), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .MakeGenericMethod(eventType);

            subscribeMethod.Invoke(this, [eventAggregator, handler, context]);
        }

        stoppingToken.Register(Dispose);

        _logger.LogInformation("EventAggregator handler {Handler} started with {Count} event subscriptions",
            typeof(THandler).Name, THandler.Types.Count());

        return Task.CompletedTask;
    }

    private void SubscribeToEnvelope<TEvent>(IEventAggregator eventAggregator, THandler handler, OperationContext context)
    {
        var token = eventAggregator.GetEvent<EventEnvelope<TId, TEvent>>()
            .Subscribe(async envelope =>
            {
                try
                {
                    var metadata = _plumber.MetadataFactory.Create(context, envelope.Event!, envelope.RecipientId);
                    await handler.Handle(metadata, envelope.Event!);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error handling {EventType} for {RecipientId} in {Handler}",
                        typeof(TEvent).Name, envelope.RecipientId, typeof(THandler).Name);
                }
            }, keepSubscriberReferenceAlive: true);

        _subscriptions.Add(token);
    }

    private void Dispose()
    {
        foreach (var sub in _subscriptions)
            sub.Dispose();
        _subscriptions.Clear();
        _scope?.Dispose();
        _scope = null;
    }
}
