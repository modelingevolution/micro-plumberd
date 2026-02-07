using MicroPlumberd;
using MicroPlumberd.Services;
using ModelingEvolution.EventAggregator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MicroPlumberd.Services.EventAggregator;

/// <summary>
/// Starts an event handler that receives events from EventAggregator instead of EventStore.
/// Creates a single scoped container for the handler and subscribes to all event types.
/// <para>
/// When <see cref="EventAggregatorPropagationRegistry"/> is registered (via
/// <see cref="ContainerExtensions.AddEventAggregatorPropagation{TEvent,TId}"/>),
/// the starter uses the propagation EA stored on <see cref="PlumberEngine"/>
/// via <c>GetExtension&lt;EventAggregatorPropagation&gt;()</c>. This enables fast
/// in-process event delivery from <c>plumber.AppendEvent()</c>.
/// Otherwise it falls back to the DI-scoped <see cref="IEventAggregator"/>.
/// </para>
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
        var context = _scope.ServiceProvider.GetRequiredService<OperationContext>();

        // Use the propagation EA from PlumberEngine extensions if propagation is configured;
        // otherwise fall back to DI-scoped EA.
        var registry = _serviceProvider.GetService<EventAggregatorPropagationRegistry>();
        IEventAggregator eventAggregator;
        string source;

        if (registry != null)
        {
            var propagation = _serviceProvider.GetRequiredService<EventAggregatorPropagation>();
            _plumber.SetExtension(propagation);
            registry.ApplyTo(propagation);
            propagation.EnsureHookInstalled(_plumber);
            eventAggregator = propagation.EventAggregator;
            source = "propagation";
        }
        else
        {
            eventAggregator = _scope.ServiceProvider.GetRequiredService<IEventAggregator>();
            source = "DI-scoped";
        }

        foreach (var eventType in THandler.Types)
        {
            var subscribeMethod = typeof(EventAggregatorEventHandlerStarter<THandler, TId>)
                .GetMethod(nameof(SubscribeToEnvelope), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .MakeGenericMethod(eventType);

            subscribeMethod.Invoke(this, [eventAggregator, handler, context]);
        }

        stoppingToken.Register(Dispose);

        _logger.LogInformation("EventAggregator handler {Handler} started with {Count} event subscriptions ({Source} EA)",
            typeof(THandler).Name, THandler.Types.Count(), source);

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
