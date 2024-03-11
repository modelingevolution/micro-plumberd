using MicroPlumberd.DirectConnect;

namespace MicroPlumberd.Tests;

[Aggregate]
public partial class FooAggregate(Guid id) : AggregateBase<FooAggregate.FooState>(id)
{
    internal new FooState State => base.State;
    public record FooState { public string Name { get; set; } };
    private static FooState Given(FooState state, FooCreated ev) => state with { Name = ev.Name };
    private static FooState Given(FooState state, FooUpdated ev) => state with { Name =ev.Name };
    public void Open(string msg) => AppendPendingChange(new FooCreated() { Name = msg });
    public void Change(string msg) => AppendPendingChange(new FooUpdated() { Name = msg });
}
public class FooCreated { public string Name { get; set; } }
public class FooUpdated { public string Name { get; set; } }


[EventHandler]
public partial class FooModel
{
    public readonly Dictionary<Guid, string> Index = new();
    private async Task Given(Metadata m, FooCreated ev)
    {
        Index.Add(m.Id, ev.Name);
    }
    private async Task Given(Metadata m, FooUpdated ev)
    {
        Index[m.Id] = ev.Name;
    }
}
