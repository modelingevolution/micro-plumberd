using System.Diagnostics;
using MicroPlumberd.Services;
using MicroPlumberd.Tests.App.Domain;

using BooAggregate = MicroPlumberd.Tests.App.Domain.BooAggregate;

namespace MicroPlumberd.Tests.App.Srv;

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