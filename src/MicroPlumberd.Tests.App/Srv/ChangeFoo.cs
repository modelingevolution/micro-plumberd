using MicroPlumberd.Services;
using MicroPlumberd.Tests.App.Domain;
using ProtoBuf;

namespace MicroPlumberd.Tests.App.Srv;

[ProtoContract]
[ThrowsFaultException<BusinessFault>]
public class RefineFoo : IId
{
    [ProtoMember(2)]
    public string? Name { get; set; }
    [ProtoMember(1)]
    public Guid Id { get; set; } = Guid.NewGuid();
}
[ProtoContract]
[ThrowsFaultException<BusinessFault>]
public class RefineBoo : IId
{
    [ProtoMember(2)]
    public string? Name { get; set; }
    [ProtoMember(1)]
    public Guid Id { get; set; } = Guid.NewGuid();
}