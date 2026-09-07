using System.Collections.Immutable;

namespace MicroPlumberd.Migration;

/// <summary>
/// The migration run's live status + progress. A single instance is owned by a <see cref="MigrationRunner"/>
/// (see <see cref="MigrationRunner.Progress"/>); the runner and its copy/drain phases push updates into it and
/// consumers pull <see cref="CurrentStatus"/> (e.g. a REST endpoint serializes it) or subscribe via
/// <see cref="ProgressChanged"/> / <see cref="IObservable{T}"/> (e.g. a Blazor page updates live).
/// </summary>
/// <remarks>
/// Thread-safe: all mutable state is guarded by a single lock and every read returns a fresh immutable
/// <see cref="MigrationStatusSnapshot"/>, so <see cref="CurrentStatus"/> is safe to read concurrently while the
/// run proceeds. The chatty per-event updates are coalesced: internal counters advance on every event, but
/// <see cref="ProgressChanged"/>/observers are notified at most once per <see cref="PublishThrottle"/> (plus
/// always on phase/state transitions), so a million-event copy does not raise a million notifications.
/// </remarks>
public sealed class MigrationProgressTracker : IObservable<MigrationStatusSnapshot>
{
    /// <summary>Minimum wall-clock gap between throttled progress notifications during the event copy.</summary>
    public static readonly TimeSpan PublishThrottle = TimeSpan.FromMilliseconds(250);

    private readonly object _gate = new();
    private readonly List<IObserver<MigrationStatusSnapshot>> _observers = new();

    // Mutable run state (all access under _gate).
    private MigrationRunState _runState = MigrationRunState.Pending;
    private MigrationPhase _phase = MigrationPhase.NotStarted;
    private bool _dryRun;
    private string? _runningMigrationId;
    private readonly List<MutableItem> _items = new();
    // Event-copy progress is held as flat mutable fields (NOT a record) so the hot per-event report
    // allocates NOTHING — the EventCopyProgress record is materialised only when a snapshot is built.
    private bool _eventCopyStarted;
    private long _eventsCopied;
    private string? _eventCurrentStream;
    private ulong _eventCurrentPos;
    private ulong _eventHeadPos;
    private DateTime? _startedUtc;
    private DateTime? _finishedUtc;
    private string? _error;
    private static readonly long ThrottleMs = (long)PublishThrottle.TotalMilliseconds;
    private long _lastPublishTicks = long.MinValue;

    private sealed class MutableItem
    {
        public required string Id;
        public required string Name;
        public MigrationItemState State;
        public DateTime? StartedUtc;
        public DateTime? FinishedUtc;
        public string? Error;
    }

    /// <summary>Raised whenever the status changes (throttled during the event copy). The argument is a fresh snapshot.</summary>
    public event EventHandler<MigrationStatusSnapshot>? ProgressChanged;

    /// <summary>A thread-safe, immutable snapshot of the run's current status and per-phase progress.</summary>
    public MigrationStatusSnapshot CurrentStatus
    {
        get { lock (_gate) return BuildSnapshotLocked(); }
    }

    /// <summary>Rx subscription: the observer receives the current snapshot immediately, then every change.</summary>
    public IDisposable Subscribe(IObserver<MigrationStatusSnapshot> observer)
    {
        MigrationStatusSnapshot snap;
        lock (_gate)
        {
            _observers.Add(observer);
            snap = BuildSnapshotLocked();
        }
        observer.OnNext(snap);
        return new Unsubscriber(this, observer);
    }

    // ---- internal reporting API (called by the runner + copy/drain phases) -------------------------------

    internal void SetMigrations(IEnumerable<(string Id, string Name)> applied,
        IEnumerable<(string Id, string Name)> pending)
    {
        lock (_gate)
        {
            _items.Clear();
            foreach (var (id, name) in applied)
                _items.Add(new MutableItem { Id = id, Name = name, State = MigrationItemState.Completed });
            foreach (var (id, name) in pending)
                _items.Add(new MutableItem { Id = id, Name = name, State = MigrationItemState.Pending });
        }
        Publish(force: true);
    }

    internal void BeginRun(bool dryRun, DateTime startedUtc)
    {
        lock (_gate)
        {
            _dryRun = dryRun;
            _runState = MigrationRunState.Running;
            _startedUtc = startedUtc;
            _phase = MigrationPhase.Planning;
        }
        Publish(force: true);
    }

    internal void EnterPhase(MigrationPhase phase)
    {
        lock (_gate) _phase = phase;
        Publish(force: true);
    }

    /// <summary>Marks all pending migrations Running (they are applied together in the single copy pass).</summary>
    internal void MarkPendingRunning(DateTime startedUtc)
    {
        lock (_gate)
        {
            string? first = null;
            foreach (var i in _items.Where(i => i.State == MigrationItemState.Pending))
            {
                i.State = MigrationItemState.Running;
                i.StartedUtc = startedUtc;
                first ??= i.Id;
            }
            _runningMigrationId = first;
        }
        Publish(force: true);
    }

    internal void MarkPendingCompleted(DateTime finishedUtc)
    {
        lock (_gate)
        {
            foreach (var i in _items.Where(i => i.State == MigrationItemState.Running))
            {
                i.State = MigrationItemState.Completed;
                i.FinishedUtc = finishedUtc;
            }
            _runningMigrationId = null;
        }
        Publish(force: true);
    }

    internal void EventCopyPlan(ulong headCommitPosition)
    {
        lock (_gate)
        {
            _eventCopyStarted = true;
            _eventHeadPos = headCommitPosition;
            _eventsCopied = 0;
            _eventCurrentStream = null;
            _eventCurrentPos = 0;
        }
        Publish(force: true);
    }

    // HOT PATH — called once per source event. Updates flat fields only (no record/LINQ/closure/boxing
    // allocation) and throttles the notification, so a million-event copy costs a lock + a few field writes.
    internal void EventCopied(long eventsCopied, string currentSourceStream, ulong currentCommitPosition)
    {
        lock (_gate)
        {
            _eventsCopied = eventsCopied;
            _eventCurrentStream = currentSourceStream;
            _eventCurrentPos = currentCommitPosition;
        }
        Publish(force: false); // throttled
    }

    internal void Complete(DateTime finishedUtc)
    {
        lock (_gate)
        {
            _runState = MigrationRunState.Completed;
            _phase = MigrationPhase.Completed;
            _finishedUtc = finishedUtc;
        }
        Publish(force: true);
    }

    internal void Fail(string error, DateTime finishedUtc)
    {
        lock (_gate)
        {
            _runState = MigrationRunState.Failed;
            _phase = MigrationPhase.Failed;
            _finishedUtc = finishedUtc;
            _error = error;
            foreach (var i in _items.Where(i => i.State == MigrationItemState.Running))
            {
                i.State = MigrationItemState.Failed;
                i.FinishedUtc = finishedUtc;
                i.Error = error;
            }
            _runningMigrationId = null;
        }
        Publish(force: true);
    }

    // ---- snapshot + notification -------------------------------------------------------------------------

    private MigrationStatusSnapshot BuildSnapshotLocked()
    {
        var items = _items
            .Select(i => new MigrationItemStatus(i.Id, i.Name, i.State, i.StartedUtc, i.FinishedUtc, i.Error))
            .ToImmutableArray();
        // Materialise the event-copy record only here (snapshot build), never on the per-event hot path.
        var eventCopy = _eventCopyStarted
            ? new EventCopyProgress(_eventsCopied, _eventCurrentStream, _eventCurrentPos, _eventHeadPos)
            : null;
        return new MigrationStatusSnapshot(_runState, _phase, _dryRun, _runningMigrationId, items,
            eventCopy, _startedUtc, _finishedUtc, _error);
    }

    private void Publish(bool force)
    {
        MigrationStatusSnapshot snap;
        IObserver<MigrationStatusSnapshot>[] observers;
        lock (_gate)
        {
            // On the throttled (early-return) path nothing is allocated — no snapshot, no observer copy.
            var now = Environment.TickCount64;
            if (!force && now - _lastPublishTicks < ThrottleMs) return;
            _lastPublishTicks = now;
            snap = BuildSnapshotLocked();
            observers = _observers.Count == 0 ? Array.Empty<IObserver<MigrationStatusSnapshot>>() : _observers.ToArray();
        }
        ProgressChanged?.Invoke(this, snap);
        foreach (var o in observers) o.OnNext(snap);
    }

    private sealed class Unsubscriber(MigrationProgressTracker owner, IObserver<MigrationStatusSnapshot> observer)
        : IDisposable
    {
        public void Dispose()
        {
            lock (owner._gate) owner._observers.Remove(observer);
        }
    }
}
