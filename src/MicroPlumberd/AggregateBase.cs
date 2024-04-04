using EventStore.Client;
using System.Linq;

namespace MicroPlumberd;

public interface IStatefull
{
    object State { get; }
    void Initialize(object state, StateInfo version);
    Type SnapshotType { get;  }
    StateInfo? InitializedWith { get; }
}

public readonly struct StateInfo
{
    public long Version { get; init; }
    public DateTimeOffset Created { get; init; }

    public StateInfo(long version, DateTimeOffset created)
    {
        Version = version;
        Created = created;
    }
}

public interface IStatefull<out T>
{
    T State { get; }
}



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
    protected TState State { get; private set; } = new();
    TState IStatefull<TState>.State => State;
    public Guid Id { get; } = id;

    public long Version { get; private set; } = -1;

    public IReadOnlyList<object> PendingEvents => _pendingEvents;

    protected void AppendPendingChange(object ev)
    {
        _pendingEvents.Add(ev);
        Apply(ev);
    }

    public async Task Rehydrate(IAsyncEnumerable<object> events)
    {
        await foreach (var e in events)
        {
            State = Apply(e);
            Version += 1;
        }
    }
    public void AckCommitted()
    {
        Version += _pendingEvents.Count;
        _pendingEvents.Clear();
    }
    protected abstract TState Given(TState state, object ev);

    private TState Apply(object ev)
    {
        return Given(State, ev);
    }
}