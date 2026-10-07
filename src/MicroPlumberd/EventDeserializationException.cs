namespace MicroPlumberd;

/// <summary>
/// A stored event could not be turned into its CLR type (or its metadata could not be parsed).
/// <para>
/// Thrown only from the read of an event whose bytes are already in memory, so the same event fails the
/// same way on every retry: an error-handle policy can recognise it by this TYPE and skip the event rather
/// than retry it for ever. The original failure is the <see cref="Exception.InnerException"/>. The message
/// names the event but never quotes its body.
/// </para>
/// </summary>
public sealed class EventDeserializationException : Exception
{
    public EventDeserializationException(string streamId, string eventType, long eventNumber, Exception inner)
        : base($"Stored event '{eventType}' #{eventNumber} in stream '{streamId}' could not be deserialized ({inner.GetType().Name}).", inner)
    {
        StreamId = streamId;
        EventType = eventType;
        EventNumber = eventNumber;
    }

    /// <summary>The event's own stream.</summary>
    public string StreamId { get; }

    /// <summary>The stored event type name.</summary>
    public string EventType { get; }

    /// <summary>The event's number (revision) in <see cref="StreamId"/>.</summary>
    public long EventNumber { get; }
}
