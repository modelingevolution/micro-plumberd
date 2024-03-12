using ProtoBuf;

namespace MicroPlumberd.Tests.AppSrc;

[ProtoContract]
public class BusinessFault { [ProtoMember(1)] public string? Name { get; init; } }