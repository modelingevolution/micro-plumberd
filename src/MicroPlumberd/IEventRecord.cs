namespace MicroPlumberd;

/// <summary>
/// Represents an event record containing both the event data and its associated metadata.
/// </summary>
public interface IEventRecord
{
    /// <summary>
    /// Gets the metadata associated with this event.
    /// </summary>
    /// <value>The <see cref="Metadata"/> containing information about the event's context, correlation, and causation.</value>
    Metadata Metadata { get; }

    /// <summary>
    /// Gets the actual event data.
    /// </summary>
    /// <value>The event object containing the domain-specific event information.</value>
    object Event { get; }
}