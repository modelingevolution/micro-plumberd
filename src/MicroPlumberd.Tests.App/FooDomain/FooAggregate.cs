using System.Data.SqlTypes;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using MicroPlumberd.Encryption;
using MicroPlumberd.Services;
using MicroPlumberd.Services.Uniqueness;
using ModelingEvolution.DirectConnect;
using ProtoBuf;

namespace MicroPlumberd.Tests.App.Domain;

public record FooEntityState {
    [JsonIgnore]
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; }
    [JsonIgnore]
    public long Version { get; set; } = -1;
}

[Aggregate]
[ThrowsFaultException<BusinessFaultException>()]
public partial class FooAggregate(Guid id) : AggregateBase<Guid,FooAggregate.FooState>(id)
{
    public new FooState State => base.State;
    public record FooState { public string? Name { get; set; } };

    
    private static FooState Given(FooState state, FooCreated ev) => state with { Name = ev.Name };

    private static FooState Given(FooState state, FooRefined ev) => state with { Name =ev.Name };
    public static FooAggregate Open(string msg, Guid id)
    {
        var r = FooAggregate.Empty(id);
        r.AppendPendingChange(new FooCreated() { Name = msg });
        return r;
    }

    public static FooAggregate Open(string msg) => Open(msg, Guid.NewGuid());

    public void Refine(string msg)
    {
        if (msg == "error") throw new BusinessFaultException("Houston we have a problem!");
        AppendPendingChange(new FooRefined() { Name = msg });
    }
}
[Aggregate(SnapshotEvery = 50)]
public partial class BooAggregate(Guid id) : AggregateBase<Guid, BooAggregate.BooState>(id)
{
    internal new BooState State => base.State;
    [ProtoContract] public record BooState { [ProtoMember(1)] public string? Name { get; set; } };
    private static BooState Given(BooState state, BooCreated ev) => state with { Name = ev.Name };
    private static BooState Given(BooState state, BooRefined ev) => state with { Name = ev.Name };
    public static BooAggregate Open(string msg)
    {
        var r = BooAggregate.Empty(msg.ToGuid());
        r.AppendPendingChange(new BooCreated() { Name = msg });
        return r;
    }

    public void Refine(string msg) => AppendPendingChange(new BooRefined() { Name = msg });
}


public record FooCreated { [Unique<FooCategory>] public string? Name { get; set; }  }
public record FooRefined { public string? Name { get; set; } }

[Unique<BooCategory>()]
[ProtoContract]
public class BooCreated { [ProtoMember(1)] public string? Name { get; set; } }

[Unique<BooCategory>()]
[ProtoContract]
public class BooRefined { [ProtoMember(1)] public string? Name { get; set; } }

record FooCategory;
record BooCategory(string Name) : IUniqueFrom<BooCategory, BooCreated>, IUniqueFrom<BooCategory, BooRefined>
{
    public static BooCategory From(BooCreated x) => new(x.Name);
    public static BooCategory From(BooRefined x) => new(x.Name);
}