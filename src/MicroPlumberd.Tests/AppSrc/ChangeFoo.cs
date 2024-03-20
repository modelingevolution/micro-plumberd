using MicroPlumberd.DirectConnect;
using MicroPlumberd.Services;
using ProtoBuf;

namespace MicroPlumberd.Tests.AppSrc;

[ProtoContract]
[ThrowsFaultCommandException<BusinessFault>]
public class ChangeFoo : IId
{
    [ProtoMember(2)]
    public string? Name { get; set; }
    [ProtoMember(1)]
    public Guid Id { get; set; } = Guid.NewGuid();
}
[ProtoContract]
[ThrowsFaultCommandException<BusinessFault>]
public class ChangeBoo : IId
{
    [ProtoMember(2)]
    public string? Name { get; set; }
    [ProtoMember(1)]
    public Guid Id { get; set; } = Guid.NewGuid();
}