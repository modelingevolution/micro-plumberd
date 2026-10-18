namespace MicroPlumberd.Tests.App.SnapshotDomain;

/// <summary>
/// A snapshotting aggregate whose state SHOWS a re-fold (issue #28): every TallyAdded increments Count and appends its Mark,
/// so an event applied twice reads as "abb" / Count 3, never as the same last-write-wins value.
/// SnapshotEvery is large: the rows write their snapshots themselves.
/// </summary>
[Aggregate(SnapshotEvery = 1000)]
public partial class TallyAggregate(Guid id) : AggregateBase<Guid, TallyAggregate.TallyState>(id)
{
    public new TallyState State => base.State;
    public record TallyState { public int Count { get; set; } public string Seen { get; set; } = ""; }

    private static TallyState Given(TallyState state, TallyAdded ev) => state with { Count = state.Count + 1, Seen = state.Seen + ev.Mark };

    /// <summary>A type "added in a later release": the build that wrote a pre-#26 snapshot did not map it; this one does.</summary>
    private static TallyState Given(TallyState state, TallyNoted ev) => state with { Count = state.Count + 1, Seen = state.Seen + ev.Mark };

    public static TallyAggregate Start(Guid id, string mark)
    {
        var r = Empty(id);
        r.AppendPendingChange(new TallyAdded { Mark = mark });
        return r;
    }

    public void Add(string mark) => AppendPendingChange(new TallyAdded { Mark = mark });
}

public record TallyAdded { public string? Mark { get; set; } }
public record TallyNoted { public string? Mark { get; set; } }
