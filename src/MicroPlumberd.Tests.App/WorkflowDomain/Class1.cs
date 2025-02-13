using MicroPlumberd.DirectConnect;
using MicroPlumberd.Services;
using MicroPlumberd.Tests.App.Domain;
using ProtoBuf;

namespace MicroPlumberd.Tests.App.WorkflowDomain;

[ProtoContract]
[ThrowsFaultException<BusinessFault>]
[Returns<HandlerOperationStatus>]
public class StartWorkflow : IId<Guid>
{
    [ProtoMember(2)]
    public string? Name { get; set; }
    [ProtoMember(1)]
    public Guid Id { get; set; } = Guid.NewGuid();
}

[ProtoContract]
[ThrowsFaultException<BusinessFault>]
[Returns<HandlerOperationStatus>]
public class CompleteWorkflow : IId<Guid>
{
    [ProtoMember(2)]
    public string? Name { get; set; }
    [ProtoMember(1)]
    public Guid Id { get; set; } = Guid.NewGuid();
}