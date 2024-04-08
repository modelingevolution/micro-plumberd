namespace MicroPlumberd;

/// <summary>
/// Attribute that is used on aggregates. When a class is marked with this attribute, source generators will generate partial class
/// that contains all boring dispatching code and metadata for plumberd to do its job.
/// </summary>
/// <seealso cref="System.Attribute" />
[AttributeUsage(AttributeTargets.Class)]
public class AggregateAttribute : Attribute
{
    private int _snapshotEvery = -1;
    private long _snapshotAfter = -1;
    public Type? SnaphotPolicy { get; set; }

    /// <summary>
    /// Gets or sets the number of events from the last snapshot before a new one is performed.
    /// </summary>
    /// <value>
    /// The snapshot every.
    /// </value>
    public int SnapshotEvery
    {
        get => _snapshotEvery;
        set { _snapshotEvery = value;
            SnaphotPolicy = typeof(ISnapshotPolicy<>);
        }
    }
    /// <summary>
    /// Gets or sets the time from last snapshots (in seconds) before a new one is performed.
    /// </summary>
    public long SnapshotAfter
    {
        get => _snapshotAfter;
        set
        {
            _snapshotAfter = value;
            SnaphotPolicy = typeof(ISnapshotPolicy<>);
        }
    }
}

/// <summary>
/// Interface for creating snapshot policies, that manage when a snapshot is performed on an aggregate.
/// </summary>
/// <typeparam name="T"></typeparam>
public interface ISnapshotPolicy<in T> : ISnapshotPolicy
{
    bool ShouldMakeSnapshot(T aggregate, StateInfo? info);
}