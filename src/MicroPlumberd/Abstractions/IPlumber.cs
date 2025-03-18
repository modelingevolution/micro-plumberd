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

public record SubscriptionRunnerState<T>(T Value, Metadata Metadata)
{
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

    public static implicit operator T(Snapshot<T> st) => st.Data;
    internal override object Value
    {
        get => Data;
        set => Data = (T)value;
    }
}

public interface IPlumber : IPlumberApi
{
    
}

public interface IPlumberInstance : IPlumberApi
{
    
}

