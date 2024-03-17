using MicroPlumberd.DirectConnect;
using ProtoBuf;

namespace MicroPlumberd.Tests.AppSrc;

[ProtoContract]
[ThrowsFaultException<BusinessFault>]
public class ChangeFoo : IId
{
    [ProtoMember(2)]
    public string? Name { get; set; }
    [ProtoMember(1)]
    public Guid Id { get; set; } = Guid.NewGuid();
}