namespace MicroPlumberd.Tests.App.Domain;

[EventHandler]
public partial class FooProcessor(IPlumber plumber)
{
    private async Task Given(Metadata m, FooRefined ev)
    {
        var agg = FooAggregate.Open(ev.Name + "new");
        await plumber.SaveNew(agg);
    }
}