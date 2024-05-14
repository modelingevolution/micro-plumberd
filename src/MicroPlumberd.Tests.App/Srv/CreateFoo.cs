using System.ComponentModel.DataAnnotations;
using MicroPlumberd.DirectConnect;
using MicroPlumberd.Services;
using MicroPlumberd.Tests.App.Domain;
using ProtoBuf;

namespace MicroPlumberd.Tests.App.Srv;



[ProtoContract]
[ThrowsFaultException<BusinessFault>]
[Returns<HandlerOperationStatus>]
public class CreateFoo : IId<Guid>
{
    [ProtoMember(2)]
    [Required]
    public string? Name { get; set; }
    [ProtoMember(1)]
    public Guid Id { get; set; } = Guid.NewGuid();
}
[ProtoContract]
[ThrowsFaultException<BusinessFault>]
[Returns<HandlerOperationStatus>]
public class CreateStrFoo 
{
    [ProtoMember(2)]
    [Required]
    public string? Name { get; set; }
    [ProtoMember(1)]
    public Guid Id { get; set; } = Guid.NewGuid();
}


public class ValidateBoo : IId<Guid>
{
    [Required]
    [Length(5, 10)]    
    public string? Name { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
}

[ProtoContract]
[Returns<HandlerOperationStatus>]
public class CreateBoo : IId<Guid>
{
    [ProtoMember(2)]
    public string? Name { get; set; }
    [ProtoMember(1)]
    public Guid Id { get; set; } = Guid.NewGuid();
}

[ProtoContract]
[Returns<HandlerOperationStatus>]
public class CreateLoo : IId<Guid>
{
    [ProtoMember(2)]
    public string? Name { get; set; }
    [ProtoMember(1)]
    public Guid Id { get; set; } = Guid.NewGuid();
}