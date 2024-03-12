using ProtoBuf;

namespace MicroPlumberd.DirectConnect;

[ProtoContract]
public record CommandEnvelope<TCommand> where TCommand :ICommand
{
    [ProtoMember(1)]
    public Guid StreamId { get; init; }
    [ProtoMember(2)]
    public required TCommand Command { get; init; }
}