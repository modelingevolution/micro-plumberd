using System.Collections.Concurrent;
using ModelingEvolution.EventAggregator;

namespace MicroPlumberd.Services.EventAggregator;

/// <summary>
/// Stores registrations for event types that should be propagated via EventAggregator
/// as a fast delivery channel when <c>plumber.AppendEvent()</c> is called.
/// Populated during DI setup, applied to <see cref="EventAggregatorPropagation"/> at runtime.
/// </summary>
internal class EventAggregatorPropagationRegistry
{
    private readonly List<Action<EventAggregatorPropagation>> _registrations = new();

    public void Register<TEvent, TId>(bool broadcast) where TId : IParsable<TId>
    {
        _registrations.Add(p => p.Register<TEvent, TId>(broadcast));
    }

    public void ApplyTo(EventAggregatorPropagation propagation)
    {
        foreach (var reg in _registrations) reg(propagation);
    }
}

/// <summary>
/// Provides fast in-process event delivery via EventAggregator as a side-channel
/// alongside EventStore persistence. Stored as an extension on <see cref="PlumberEngine"/>
/// via <c>engine.SetExtension(propagation)</c>.
/// <para>
/// When <c>plumber.AppendEvent()</c> is called for a registered event type, the event is
/// immediately published on the local <see cref="IEventAggregator"/> — delivering it to
/// <see cref="IEventHandler"/> subscribers before the EventStore write completes.
/// EventStore is always written to regardless — this is purely a fast delivery channel.
/// </para>
/// <para>
/// If <c>broadcast</c> is enabled for an event type, the event is also broadcast
/// via <see cref="IEventAggregatorPool"/> to all circuits.
/// </para>
/// </summary>
public class EventAggregatorPropagation
{
    private readonly IEventAggregator _ea;
    private readonly IEventAggregatorPool _pool;
    private readonly ConcurrentDictionary<Type, PropagationEntry> _registry = new();
    private int _hookInstalled;

    /// <summary>
    /// Creates a manually-managed EventAggregator (not from DI) using the given pool.
    /// Stored on <see cref="PlumberEngine"/> via <c>SetExtension</c>.
    /// </summary>
    public EventAggregatorPropagation(IEventAggregatorPool pool)
    {
        _pool = pool;
        _ea = new ModelingEvolution.EventAggregator.EventAggregator(
            new NullForwarder(), (EventAggregatorPool)pool);
    }

    /// <summary>
    /// The EventAggregator instance used for fast in-process delivery.
    /// <see cref="EventAggregatorEventHandlerStarter{THandler,TId}"/> subscribes to this.
    /// </summary>
    public IEventAggregator EventAggregator => _ea;

    /// <summary>
    /// Registers an event type for EA propagation. Called during initialization
    /// with pre-built generic delegates so that no reflection is needed at runtime.
    /// </summary>
    public void Register<TEvent, TId>(bool broadcast) where TId : IParsable<TId>
    {
        Func<IEventAggregatorPool, object?, object, Task>? broadcastFn = broadcast
            ? (pool, id, evt) => pool.Broadcast(new EventEnvelope<TId, TEvent>((TId)id!, (TEvent)evt))
            : null;

        _registry[typeof(TEvent)] = new PropagationEntry(
            (ea, id, evt) => ea.GetEvent<EventEnvelope<TId, TEvent>>()
                .Publish(new EventEnvelope<TId, TEvent>((TId)id!, (TEvent)evt)),
            broadcastFn);
    }

    /// <summary>
    /// Ensures the <see cref="PlumberEngine.EventAppending"/> hook is installed exactly once.
    /// </summary>
    internal void EnsureHookInstalled(PlumberEngine engine)
    {
        if (Interlocked.CompareExchange(ref _hookInstalled, 1, 0) == 0)
            engine.EventAppending += OnEventAppendingAsync;
    }

    /// <summary>
    /// Hook handler for <see cref="PlumberEngine.EventAppending"/>.
    /// Checks if the event type is registered and publishes on EA.
    /// </summary>
    internal async Task OnEventAppendingAsync(OperationContext context, object evt, object? id, object? metadata)
    {
        if (!_registry.TryGetValue(evt.GetType(), out var entry))
            return;

        if (entry.Broadcaster != null)
            // Broadcast via pool — the local EA receives it through the pool too
            await entry.Broadcaster(_pool, id, evt);
        else
            // Local-only delivery via EA (no pool broadcast)
            await entry.Publisher(_ea, id, evt);
    }

    private record PropagationEntry(
        Func<IEventAggregator, object?, object, Task> Publisher,
        Func<IEventAggregatorPool, object?, object, Task>? Broadcaster);
}
