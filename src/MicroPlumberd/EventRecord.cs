namespace MicroPlumberd;

/// <summary>
/// Represents a strongly-typed event record containing event data and metadata.
/// </summary>
/// <typeparam name="TEvent">The type of the event.</typeparam>
public interface IEventRecord<out TEvent> : IEventRecord
{
    /// <summary>
    /// Gets the strongly-typed event data.
    /// </summary>
    /// <value>The event of type <typeparamref name="TEvent"/>.</value>
    new TEvent Event { get; }
}

/// <summary>
/// Represents an event record containing event data and its associated metadata.
/// </summary>
/// <typeparam name="TEvent">The type of the event.</typeparam>
record EventRecord<TEvent> : IEventRecord<TEvent>
{
    /// <summary>
    /// Gets or initializes the metadata associated with this event.
    /// </summary>
    public Metadata Metadata { get; init; }

    /// <summary>
    /// Gets or initializes the event data.
    /// </summary>
    public TEvent Event { get; init; }

    /// <summary>
    /// Gets the event as an object.
    /// </summary>
    object IEventRecord.Event => Event;
}