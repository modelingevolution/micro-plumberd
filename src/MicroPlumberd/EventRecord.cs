namespace MicroPlumberd;

public interface IEventRecord<out TEvent> : IEventRecord
{
    new TEvent Event { get; }
}

record EventRecord<TEvent> : IEventRecord<TEvent>
{
    public Metadata Metadata { get; init; }
    public TEvent Event { get; init; }
    object IEventRecord.Event => Event;
}