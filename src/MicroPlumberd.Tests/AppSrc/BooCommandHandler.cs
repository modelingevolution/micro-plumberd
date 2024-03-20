using System.Diagnostics;
using MicroPlumberd.DirectConnect;
using MicroPlumberd.Services;

namespace MicroPlumberd.Tests.AppSrc;

[CommandHandler]
public partial class BooCommandHandler(IPlumber plumber)
{
    public async Task Handle(Guid id, CreateBoo cmd)
    {
        if (cmd.Name == "error")
            throw new BusinessFaultException("Boo");

        var agg = BooAggregate.New(id);
        agg.Open(cmd.Name!);

        await plumber.SaveNew(agg);
        Debug.WriteLine("BooCreated");
    }


    [ThrowsFaultCommandException<BusinessFault>]
    public async Task Handle(Guid id, ChangeBoo cmd)
    {
        if (cmd.Name == "error")
            throw new BusinessFaultException("Boo");

        var agg = await plumber.Get<BooAggregate>(id);
        agg.Change(cmd.Name!);

        await plumber.SaveChanges(agg);
        Debug.WriteLine("BooChanged");
    }

}