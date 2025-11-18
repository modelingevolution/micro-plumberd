using MicroPlumberd.Api;

namespace MicroPlumberd;

/// <summary>
/// Represents a snapshot object used in Plumberd.
/// </summary>
public interface ISnapshot
{
    /// <summary>
    /// Gets the data of the snapshot.
    /// </summary>
    object Data { get; }
    
    /// <summary>
    /// Gets the creation date of the snapshot.
    /// </summary>
    DateTimeOffset Created { get; }
    
    /// <summary>
    /// Gets the version of the snapshot.
    /// </summary>
    long Version { get; }
}

/// <summary>
/// Represents the state of a subscription runner containing a value and its associated metadata.
/// </summary>
/// <typeparam name="T">The type of the value.</typeparam>
/// <param name="Value">The current value.</param>
/// <param name="Metadata">The metadata associated with this value.</param>
public record SubscriptionRunnerState<T>(T Value, Metadata Metadata)
{
    /// <summary>
    /// Implicitly converts a <see cref="SubscriptionRunnerState{T}"/> to its underlying value.
    /// </summary>
    /// <param name="st">The subscription runner state to convert.</param>
    public static implicit operator T?(SubscriptionRunnerState<T>? st) => st != null ? st.Value : default;
}
/// <summary>
/// Represents a snapshot object used in Plumberd.
/// </summary>
public abstract record Snapshot
{
    internal abstract object Value { get; set; }
    
    /// <summary>
    /// Gets the creation date of the snapshot.
    /// </summary>
    public DateTimeOffset Created { get; internal set; }
    
    /// <summary>
    /// Gets the version of the snapshot.
    /// </summary>
    public long Version { get; internal set; }
}

/// <summary>
/// Represents a generic snapshot object used in Plumberd.
/// </summary>
/// <typeparam name="T">The type of the snapshot data.</typeparam>
public sealed record Snapshot<T> : Snapshot, ISnapshot
{
    object ISnapshot.Data => Data;
    
    /// <summary>
    /// Gets the data of the snapshot.
    /// </summary>
    public T Data { get; internal set; }

    /// <summary>
    /// Implicitly converts a <see cref="Snapshot{T}"/> to its underlying data.
    /// </summary>
    /// <param name="st">The snapshot to convert.</param>
    public static implicit operator T(Snapshot<T> st) => st.Data;

    internal override object Value
    {
        get => Data;
        set => Data = (T)value;
    }
}

/// <summary>
/// Represents the main interface for interacting with the Plumber event sourcing engine.
/// Provides methods for managing aggregates, events, and subscriptions.
/// </summary>
public interface IPlumber : IPlumberApi
{

}

/// <summary>
/// Represents an instance-specific interface for interacting with the Plumber event sourcing engine.
/// Used for scoped operations within a specific context or session.
/// </summary>
public interface IPlumberInstance : IPlumberApi
{

}

