using MicroPlumberd.DirectConnect;
using ProtoBuf;

namespace MicroPlumberd.Tests.AppSrc;

[ProtoContract]
[Returns<HandlerOperationStatus>]
public class CreateFoo : ICommand
{
    [ProtoMember(2)]
    public string Name { get; set; }
    [ProtoMember(1)]
    public Guid Id { get; set; } = Guid.NewGuid();
}