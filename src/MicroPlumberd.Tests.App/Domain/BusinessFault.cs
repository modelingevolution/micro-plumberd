using ProtoBuf;

namespace MicroPlumberd.Tests.App.Domain;

[ProtoContract]
public record BusinessFault { [ProtoMember(1)] public string? Name { get; init; } }