using MicroPlumberd.Services;

namespace MicroPlumberd.Tests.AppSrc;

[EventHandler]
public partial class FooProcessor(IPlumber plumber)
{
    private async Task Given(Metadata m, FooUpdated ev)
    {
        var agg = FooAggregate.New(Guid.NewGuid());
        agg.Open(ev.Name + "new");
        await plumber.SaveNew(agg);
    }
}