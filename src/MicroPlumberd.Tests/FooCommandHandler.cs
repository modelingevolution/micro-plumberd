using MicroPlumberd.DirectConnect;
using ModelingEvolution.DirectConnect;
using ProtoBuf;

namespace MicroPlumberd.Tests;

[ProtoContract]
[Returns<HandlerOperationStatus>]
public class CreateFoo : ICommand
{
    [ProtoMember(2)]
    public string Name { get; set; }
    [ProtoMember(1)]
    public Guid Id { get; set; } = Guid.NewGuid();
}

[ProtoContract]
[ThrowsFaultException<BusinessFault>]
public class ChangeFoo : ICommand
{
    [ProtoMember(2)]
    public string Name { get; set; }
    [ProtoMember(1)]
    public Guid Id { get; set; } = Guid.NewGuid();
}


[ProtoContract]
public class BusinessFault { [ProtoMember(1)] public string Name { get; init; } }

public class BusinessFaultException : FaultException<BusinessFault>
{
    public BusinessFaultException(string name) : base(new BusinessFault() { Name = name }) { }
} 
[CommandHandler]
public partial class FooCommandHandler(IPlumber plumber)
{
    
    [ThrowsFaultException<BusinessFault>]
    public async Task Handle(Guid id, CreateFoo cmd)
    {
        if (cmd.Name == "error")
            throw new BusinessFaultException("Foo");

        var agg = FooAggregate.New(id);
        agg.Open(cmd.Name);

        await plumber.SaveNew(agg);
    }

    [ThrowsFaultException<BusinessFault>]
    public async Task<HandlerOperationStatus> Handle(Guid id, ChangeFoo cmd)
    {
        if (cmd.Name == "error")
            throw new BusinessFaultException("Foo");

        var agg = await plumber.Get<FooAggregate>(id);
        agg.Change(cmd.Name);

        await plumber.SaveChanges(agg);
        return HandlerOperationStatus.Ok();
    }
}