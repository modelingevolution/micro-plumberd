namespace MicroPlumberd.Tests.App.Domain;

[EventHandler]
public partial class FooProcessor(IPlumber plumber)
{
    private async Task Given(Metadata m, FooUpdated ev)
    {
        var agg = App.Domain.FooAggregate.New(Guid.NewGuid());
        agg.Open(ev.Name + "new");
        await plumber.SaveNew(agg);
    }
}