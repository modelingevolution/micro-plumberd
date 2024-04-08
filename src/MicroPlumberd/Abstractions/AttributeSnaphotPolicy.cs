using System.Reflection;

namespace MicroPlumberd;

/// <summary>
/// Standard snapshot policy that takes into the account time and minimal event occurence from the last taken snapshot.
/// </summary>
/// <typeparam name="T"></typeparam>
/// <seealso cref="MicroPlumberd.ISnapshotPolicy&lt;T&gt;" />
public class AttributeSnaphotPolicy<T> : ISnapshotPolicy<T>
    where T:IAggregate
{
    public AttributeSnaphotPolicy()
    {
        var att = typeof(T).GetCustomAttribute<AggregateAttribute>();
        this.MinTime = att.SnapshotAfter > 0 ? TimeSpan.FromSeconds(att.SnapshotAfter) : null;
        this.MinEventCount = att.SnapshotEvery > 0 ? att.SnapshotEvery : null;
    }

    public int? MinEventCount { get; }

    public TimeSpan? MinTime { get; }

    public virtual bool ShouldMakeSnapshot(T aggregate, StateInfo? info)
    {
        var n = DateTimeOffset.Now;
        var i = info ?? new StateInfo(-1, n);
        return MinEventCount.HasValue && aggregate.Version - i.Version >= MinEventCount.Value || 
               MinTime.HasValue && n.Subtract(i.Created) >= MinTime.Value;
    }

    public bool ShouldMakeSnapshot(object owner, StateInfo? info) => this.ShouldMakeSnapshot((T)owner, info);
}