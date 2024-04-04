using System.Data.SqlTypes;
using System.Runtime.CompilerServices;
using MicroPlumberd.Services;
using MicroPlumberd.Services.Uniqueness;

namespace MicroPlumberd.Tests.App.Domain;



[Aggregate]
[ThrowsFaultException<BusinessFaultException>()]
public partial class FooAggregate(Guid id) : AggregateBase<FooAggregate.FooState>(id)
{
    public new FooState State => base.State;
    public record FooState { public string? Name { get; set; } };

    
    private static FooState Given(FooState state, FooCreated ev) => state with { Name = ev.Name };

    private static FooState Given(FooState state, FooRefined ev) => state with { Name =ev.Name };
    public void Open(string msg) => AppendPendingChange(new FooCreated() { Name = msg });
    
    public void Refine(string msg)
    {
        if (msg == "error") throw new BusinessFaultException("Houston we have a problem!");
        AppendPendingChange(new FooRefined() { Name = msg });
    }
}
[Aggregate(SnapshotEvery = 50)]
public partial class BooAggregate(Guid id) : AggregateBase<BooAggregate.BooState>(id)
{
    internal new BooState State => base.State;
    public record BooState { public string? Name { get; set; } };
    private static BooState Given(BooState state, BooCreated ev) => state with { Name = ev.Name };
    private static BooState Given(BooState state, BooRefined ev) => state with { Name = ev.Name };
    public void Open(string msg) => AppendPendingChange(new BooCreated() { Name = msg });
    public void Refine(string msg) => AppendPendingChange(new BooRefined() { Name = msg });
}


public record FooCreated { [Unique<FooCategory>] public string? Name { get; set; } }
public record FooRefined { public string? Name { get; set; } }

[Unique<BooCategory>()]
public class BooCreated { public string? Name { get; set; } }

[Unique<BooCategory>()]
public class BooRefined { public string? Name { get; set; } }

record FooCategory;
record BooCategory(string Name) : IUniqueFrom<BooCategory, BooCreated>, IUniqueFrom<BooCategory, BooRefined>
{
    public static BooCategory From(BooCreated x) => new(x.Name);
    public static BooCategory From(BooRefined x) => new(x.Name);
}