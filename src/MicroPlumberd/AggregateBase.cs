using EventStore.Client;
using System.Linq;

namespace MicroPlumberd;

/// <summary>
/// Represents a stateful object.
/// </summary>
public interface IStatefull
{
    /// <summary>
    /// Gets the state of the object.
    /// </summary>
    object State { get; }
    
    /// <summary>
    /// Initializes the aggregate with the specified state and version information.
    /// </summary>
    /// <param name="state">The state object.</param>
    /// <param name="version">The version information.</param>
    void Initialize(object state, StateInfo version);
    
    /// <summary>
    /// Gets the type of the snapshot.
    /// </summary>
    Type SnapshotType { get;  }
    

    /// <summary>
    /// Gets the state information with which the aggregate was initialized.
    /// </summary>
    StateInfo? InitializedWith { get; }
}

/// <summary>
/// Represents the state information of an aggregate.
/// </summary>
public readonly struct StateInfo(long version, DateTimeOffset created)
{
    /// <summary>
    /// Gets or sets the version of the state.
    /// </summary>
    public long Version { get; init; } = version;

    /// <summary>
    /// Gets or sets the creation date and time of the state.
    /// </summary>
    public DateTimeOffset Created { get; init; } = created;
}

/// <summary>
/// Represents a stateful object that exposes a read-only state.
/// </summary>
/// <typeparam name="T">The type of the state.</typeparam>
public interface IStatefull<out T>
{
    /// <summary>
    /// Gets the current state of the object.
    /// </summary>
    T State { get; }
}



/// <summary>
/// Represents the base class for aggregate roots in the application.
/// </summary>
/// <typeparam name="TState">The type of the aggregate state.</typeparam>
public abstract class AggregateBase<TState>(Guid id) : IVersioned, IId, IStatefull<TState>, IStatefull
    where TState : new()

{
    private StateInfo? _initialized;
    object IStatefull.State => State;
    StateInfo? IStatefull.InitializedWith => _initialized;
    Type IStatefull.SnapshotType => typeof(TState);
    void IStatefull.Initialize(object state, StateInfo info)
    {
        State = (TState)state;
        Version = info.Version;
        _initialized = info;
    }
    private readonly List<object> _pendingEvents = new();
    
    /// <summary>
    /// Gets the state of the aggregate.
    /// </summary>
    protected TState State { get; private set; } = new();
    TState IStatefull<TState>.State => State;

    /// <summary>
    /// Gets the unique identifier of the aggregate.
    /// </summary>
    public Guid Id { get; } = id;

    /// <summary>
    /// Gets the version of the aggregate.
    /// </summary>
    public long Version { get; private set; } = -1;

    /// <summary>
    /// Gets or sets the list of pending events for the aggregate.
    /// </summary>
    public IReadOnlyList<object> PendingEvents => _pendingEvents;

    /// <summary>
    /// Appends a pending change to the list of pending events and applies the change.
    /// </summary>
    /// <param name="ev">The pending change to append.</param>
    protected void AppendPendingChange(object ev)
    {
        _pendingEvents.Add(ev);
        Apply(ev);
    }

    /// <summary>
    /// Rehydrates the aggregate by applying a sequence of events.
    /// </summary>
    /// <param name="events">The sequence of events to apply.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task Rehydrate(IAsyncEnumerable<object> events)
    {
        await foreach (var e in events)
        {
            State = Apply(e);
            Version += 1;
        }
    }
    /// <summary>
    /// Acknowledges the committed events and clears the pending events.
    /// </summary>
    public void AckCommitted()
    {
        Version += _pendingEvents.Count;
        _pendingEvents.Clear();
    }
    /// <summary>
    /// Dispatches event to create a new state.
    /// </summary>
    /// <typeparam name="TState">The type of the aggregate state.</typeparam>
    protected abstract TState Given(TState state, object ev);

    private TState Apply(object ev)
    {
        return Given(State, ev);
    }
}