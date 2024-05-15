using MicroPlumberd.DirectConnect;
using MicroPlumberd.Services;
using MicroPlumberd.Tests.App.Domain;

using FooAggregate = MicroPlumberd.Tests.App.Domain.FooAggregate;

namespace MicroPlumberd.Tests.App.Srv;

[CommandHandler]
public partial class FooCommandHandler(IPlumber plumber)
{
    public async Task Handle(Guid id, CreateFoo cmd)
    {
        if (cmd.Name == "error")
            throw new BusinessFaultException("Foo");

        var agg = FooAggregate.Open(cmd.Name, id);

        await plumber.SaveNew(agg);
    }


    [ThrowsFaultException<BusinessFault>]
    public async Task<HandlerOperationStatus> Handle(Guid id, RefineFoo cmd)
    {
        var agg = await plumber.Get<FooAggregate>(id);
        agg.Refine(cmd.Name!);

        await plumber.SaveChanges(agg);
        return HandlerOperationStatus.Ok();
    }
}
[CommandHandler]
public partial class SecretCommandHandler(IPlumber plumber)
{
    public async Task Handle(Guid id, CreateSecret cmd)
    {
        await plumber.AppendEvent(new SecretCreated() { Password = cmd.Password }, id);
    }

}