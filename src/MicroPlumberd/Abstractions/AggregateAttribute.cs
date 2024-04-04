using System.Reflection;

namespace MicroPlumberd;

[AttributeUsage(AttributeTargets.Class)]
public class AggregateAttribute : Attribute
{
    private int _snapshotEvery = -1;
    private long _snapshotAfter = -1;
    public Type? SnaphotPolicy { get; set; }

    public int SnapshotEvery
    {
        get => _snapshotEvery;
        set { _snapshotEvery = value;
            SnaphotPolicy = typeof(ISnapshotPolicy<>);
        }
    }
    /// <summary>
    /// In seconds
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

public class AttributeSnaphotPolicy<T> : ISnapshotPolicy<T>
 where T:IAggregate
{
    public AttributeSnaphotPolicy()
    {
        var att = typeof(T).GetCustomAttribute<AggregateAttribute>();
        this.MinTime = att.SnapshotAfter > 0 ? TimeSpan.FromSeconds(att.SnapshotAfter) : null;
        this.MinEventCount = att.SnapshotEvery > 0 ? att.SnapshotEvery : null;
    }

    public int? MinEventCount { get; set; }

    public TimeSpan? MinTime { get;  }

    public bool ShouldMakeSnapshot(T aggregate, StateInfo? info)
    {
        var n = DateTimeOffset.Now;
        var i = info ?? new StateInfo(-1, n);
        return MinEventCount.HasValue && aggregate.Version - i.Version >= MinEventCount.Value || 
               MinTime.HasValue && n.Subtract(i.Created) >= MinTime.Value;
    }

    public bool ShouldMakeSnapshot(object owner, StateInfo? info) => this.ShouldMakeSnapshot((T)owner, info);
}

public interface ISnapshotPolicy
{
    bool ShouldMakeSnapshot(object owner, StateInfo? info);
}
public interface ISnapshotPolicy<in T> : ISnapshotPolicy
{
    bool ShouldMakeSnapshot(T aggregate, StateInfo? info);
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public class AcceptedTypeAttribute : Attribute
{
    public AcceptedTypeAttribute(Type acceptedType)
    {
        AcceptedType = acceptedType;
    }

    public Type AcceptedType { get; init; }
}

[AttributeUsage(AttributeTargets.Class)]
public class OutputStreamAttribute : Attribute
{
    public OutputStreamAttribute(string outputStreamName)
    {
        if (string.IsNullOrWhiteSpace(outputStreamName))
            throw new ArgumentException(nameof(outputStreamName));
        this.OutputStreamName = outputStreamName;
    }

    public string OutputStreamName { get; }
}