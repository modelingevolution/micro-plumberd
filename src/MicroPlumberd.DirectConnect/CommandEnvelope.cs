using ProtoBuf;

namespace MicroPlumberd.DirectConnect;

[ProtoContract]
public record CommandEnvelope<TCommand>
{
    private Guid? _id = null;

    public Guid CommandId
    {
        get
        {
            if (Command is IId i) return i.Id;
            return _id ??= Guid.NewGuid();
        }
    }

    [ProtoMember(1)]
    public Guid StreamId { get; init; }

    [ProtoMember(2)]
    public required TCommand Command { get; init; }

    [ProtoMember(3)]
    public Guid? CorrelationId { get; init; }
}