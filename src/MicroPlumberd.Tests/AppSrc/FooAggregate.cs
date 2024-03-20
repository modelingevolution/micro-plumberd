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
[Aggregate]
public partial class BooAggregate(Guid id) : AggregateBase<BooAggregate.BooState>(id)
{
    internal new BooState State => base.State;
    public record BooState { public string? Name { get; set; } };
    private static BooState Given(BooState state, BooCreated ev) => state with { Name = ev.Name };
    private static BooState Given(BooState state, BooUpdated ev) => state with { Name = ev.Name };
    public void Open(string msg) => AppendPendingChange(new BooCreated() { Name = msg });
    public void Change(string msg) => AppendPendingChange(new BooUpdated() { Name = msg });
}
public class FooCreated { public string? Name { get; set; } }
public class FooUpdated { public string? Name { get; set; } }

public class BooCreated { public string? Name { get; set; } }
public class BooUpdated { public string? Name { get; set; } }