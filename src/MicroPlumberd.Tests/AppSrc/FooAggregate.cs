namespace MicroPlumberd.Tests.AppSrc;

[Aggregate]
public partial class FooAggregate(Guid id) : AggregateBase<FooAggregate.FooState>(id)
{
    internal new FooState State => base.State;
    public record FooState { public string? Name { get; set; } };
    private static FooState Given(FooState state, FooCreated ev) => state with { Name = ev.Name };
    private static FooState Given(FooState state, FooUpdated ev) => state with { Name =ev.Name };
    public void Open(string msg) => AppendPendingChange(new FooCreated() { Name = msg });
    public void Change(string msg) => AppendPendingChange(new FooUpdated() { Name = msg });
}
public class FooCreated { public string? Name { get; set; } }
public class FooUpdated { public string? Name { get; set; } }