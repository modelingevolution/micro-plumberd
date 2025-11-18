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
    /// <summary>
    /// Initializes a new instance of the <see cref="AttributeSnaphotPolicy{T}"/> class.
    /// Reads snapshot policy configuration from the <see cref="AggregateAttribute"/> on type T.
    /// </summary>
    public AttributeSnaphotPolicy()
    {
        var att = typeof(T).GetCustomAttribute<AggregateAttribute>();
        this.MinTime = att.SnapshotAfter > 0 ? TimeSpan.FromSeconds(att.SnapshotAfter) : null;
        this.MinEventCount = att.SnapshotEvery > 0 ? att.SnapshotEvery : null;
    }

    /// <summary>
    /// Gets the minimum number of events that must occur before a snapshot is created.
    /// </summary>
    /// <value>The minimum event count, or <c>null</c> if not configured.</value>
    public int? MinEventCount { get; }

    /// <summary>
    /// Gets the minimum time span that must elapse before a snapshot is created.
    /// </summary>
    /// <value>The minimum time span, or <c>null</c> if not configured.</value>
    public TimeSpan? MinTime { get; }

    /// <summary>
    /// Determines whether a snapshot should be created based on the aggregate's version and time since last snapshot.
    /// </summary>
    /// <param name="aggregate">The aggregate to evaluate.</param>
    /// <param name="info">Optional state information containing version and creation time of last snapshot.</param>
    /// <returns><c>true</c> if the minimum event count or minimum time criteria is met; otherwise, <c>false</c>.</returns>
    public virtual bool ShouldMakeSnapshot(T aggregate, StateInfo? info)
    {
        var n = DateTimeOffset.Now;
        var i = info ?? new StateInfo(-1, n);
        return MinEventCount.HasValue && aggregate.Version - i.Version >= MinEventCount.Value ||
               MinTime.HasValue && n.Subtract(i.Created) >= MinTime.Value;
    }

    /// <summary>
    /// Determines whether a snapshot should be created for the specified owner object.
    /// </summary>
    /// <param name="owner">The owner object to evaluate, which will be cast to type T.</param>
    /// <param name="info">Optional state information containing version and creation time of last snapshot.</param>
    /// <returns><c>true</c> if a snapshot should be created; otherwise, <c>false</c>.</returns>
    public bool ShouldMakeSnapshot(object owner, StateInfo? info) => this.ShouldMakeSnapshot((T)owner, info);
}