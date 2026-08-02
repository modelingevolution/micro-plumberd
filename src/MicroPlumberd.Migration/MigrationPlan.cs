namespace MicroPlumberd.Migration;

/// <summary>Per-migration effect counters accumulated over a run (or a dry run).</summary>
public sealed class MigrationStats
{
    /// <summary>The migration's Id.</summary>
    public required string Id { get; init; }

    /// <summary>The migration's name.</summary>
    public required string Name { get; init; }

    /// <summary>Events this migration dropped.</summary>
    public long Dropped { get; internal set; }

    /// <summary>Type/stream renames this migration performed.</summary>
    public long Renamed { get; internal set; }

    /// <summary>JSON transforms this migration performed.</summary>
    public long Transformed { get; internal set; }
}

/// <summary>
/// The composed set of PENDING migrations, applied in order to each event. Owns per-migration attribution.
/// </summary>
internal sealed class MigrationPlan
{
    private readonly (string MigId, IMigrationOp Op)[] _ops;

    public IReadOnlyList<CompiledMigration> Migrations { get; }
    public IReadOnlyDictionary<string, MigrationStats> Stats { get; }

    /// <summary>Event types that a <c>TransformJson</c> rule targets — the only types whose payload needs parsing.</summary>
    public IReadOnlySet<string> TransformTypes { get; }

    /// <summary>
    /// True if any <c>DropEvent(Func&lt;RawEvent,bool&gt;)</c> rule exists. <see cref="RawEvent"/> exposes the
    /// payload/metadata as PARSED <see cref="System.Text.Json.Nodes.JsonNode"/>, and the predicate is opaque, so
    /// any such rule conservatively forces every event to be parsed (we cannot know which fields it reads).
    /// </summary>
    public bool AnyDropEventRule { get; }

    public MigrationPlan(IReadOnlyList<CompiledMigration> migrations)
    {
        Migrations = migrations;
        _ops = migrations.SelectMany(m => m.Operations.Select(op => (m.Id, op))).ToArray();
        Stats = migrations.ToDictionary(
            m => m.Id,
            m => new MigrationStats { Id = m.Id, Name = m.Name });

        var transformTypes = new HashSet<string>(StringComparer.Ordinal);
        var anyDropEvent = false;
        foreach (var (_, op) in _ops)
        {
            switch (op)
            {
                case TransformJsonOp t: transformTypes.Add(t.Type); break;
                case DropEventOp: anyDropEvent = true; break;
            }
        }
        TransformTypes = transformTypes;
        AnyDropEventRule = anyDropEvent;
    }

    /// <summary>
    /// Whether the copy engine must parse an event of <paramref name="eventType"/> to apply the rules. False for
    /// the common case (no rule inspects/transforms the payload) → the event is copied byte-verbatim.
    /// </summary>
    public bool RequiresPayloadParse(string eventType) =>
        AnyDropEventRule || TransformTypes.Contains(eventType);

    /// <summary>Folds <paramref name="ctx"/> through every operation, updating per-migration <see cref="Stats"/>.</summary>
    public void Apply(EventContext ctx, IMigrationOpLog? log)
    {
        foreach (var (migId, op) in _ops)
        {
            var effect = op.Apply(ctx, log);
            if (effect == OpEffect.None) continue;

            var s = Stats[migId];
            switch (effect)
            {
                case OpEffect.Dropped: s.Dropped++; break;
                case OpEffect.Renamed: s.Renamed++; break;
                case OpEffect.Transformed: s.Transformed++; break;
            }
            if (ctx.Dropped) break; // nothing further can affect a dropped event
        }
    }

    /// <summary>
    /// Static stream names any operation references — validated against reserved streams before a run so a
    /// rule can never target the migration-history stream.
    /// </summary>
    public IEnumerable<string> ReferencedStreamNames =>
        _ops.SelectMany(t => t.Op.ReferencedStreamNames);

    /// <summary>New stream names that a <c>RenameStream</c> rule retargets events into (validated by the runner).</summary>
    public IEnumerable<string> RenameStreamTargets =>
        _ops.Select(t => t.Op).OfType<RenameStreamOp>().Select(o => o.NewName);

    /// <summary>New event-type names that a <c>RenameType</c> rule assigns (validated by the runner).</summary>
    public IEnumerable<string> RenameTypeTargets =>
        _ops.Select(t => t.Op).OfType<RenameTypeOp>().Select(o => o.NewType);
}
