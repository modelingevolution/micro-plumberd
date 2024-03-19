
using MicroPlumberd.DirectConnect;
using MicroPlumberd.Services;
using ProtoBuf;

namespace MicroPlumberd.Tests.AppSrc;

[ProtoContract]
[ThrowsFaultCommandException<BusinessFault>]
[Returns<HandlerOperationStatus>]
public class CreateFoo : IId
{
    [ProtoMember(2)]
    public string? Name { get; set; }
    [ProtoMember(1)]
    public Guid Id { get; set; } = Guid.NewGuid();
}

[ProtoContract]
[Returns<HandlerOperationStatus>]
public class CreateBoo : IId
{
    [ProtoMember(2)]
    public string? Name { get; set; }
    [ProtoMember(1)]
    public Guid Id { get; set; } = Guid.NewGuid();
}

[ProtoContract]
[Returns<HandlerOperationStatus>]
public class CreateLoo : IId
{
    [ProtoMember(2)]
    public string? Name { get; set; }
    [ProtoMember(1)]
    public Guid Id { get; set; } = Guid.NewGuid();
}