using System.Diagnostics;
using MicroPlumberd.DirectConnect;
using MicroPlumberd.Services;

namespace MicroPlumberd.Tests.AppSrc;

[CommandHandler]
public partial class FooCommandHandler(IPlumber plumber)
{

    
    public async Task Handle(Guid id, CreateFoo cmd)
    {
        if (cmd.Name == "error")
            throw new BusinessFaultException("Foo");

        var agg = FooAggregate.New(id);
        agg.Open(cmd.Name!);

        await plumber.SaveNew(agg);
    }


    [ThrowsFaultCommandException<BusinessFault>]
    public async Task<HandlerOperationStatus> Handle(Guid id, ChangeFoo cmd)
    {
        if (cmd.Name == "error")
            throw new BusinessFaultException("Foo");

        var agg = await plumber.Get<FooAggregate>(id);
        agg.Change(cmd.Name!);

        await plumber.SaveChanges(agg);
        return HandlerOperationStatus.Ok();
    }

}