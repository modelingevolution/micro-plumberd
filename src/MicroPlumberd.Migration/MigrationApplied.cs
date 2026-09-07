namespace MicroPlumberd.Migration;

/// <summary>
/// The history record written to the migration-history stream once a migration has been applied to the
/// destination store. One record per applied migration; the history travels with the store.
/// </summary>
public sealed record MigrationApplied
{
    /// <summary>The migration's <see cref="Migration.Id"/>.</summary>
    public required string Id { get; init; }

    /// <summary>The migration's human-readable name.</summary>
    public required string Name { get; init; }

    /// <summary>Checksum of the migration's rules at the time it was applied (see <see cref="CompiledMigration"/>).</summary>
    public required string Checksum { get; init; }

    /// <summary>When the migration run that applied this migration started (UTC).</summary>
    public required DateTime AppliedAtUtc { get; init; }

    /// <summary>Total user events scanned from the source in the run that applied this migration.</summary>
    public required long SourceEvents { get; init; }

    /// <summary>Total user events written to the destination in that run.</summary>
    public required long Kept { get; init; }

    /// <summary>Events this migration dropped.</summary>
    public required long Dropped { get; init; }

    /// <summary>Events this migration transformed (JSON transforms + type/stream renames).</summary>
    public required long Transformed { get; init; }

    /// <summary>
    /// The descriptors of the operations this migration compiled to, in declaration order (e.g.
    /// <c>DropStreamPredicate(…)</c>, <c>RenameType("A"-&gt;"B")</c>, <c>Transform(…)</c>) — so an operator
    /// reading the history of a store can see WHAT a past rewrite did, not only that it happened.
    /// </summary>
    /// <remarks>
    /// OPTIONAL by contract (RUNBOOK m10): history written by an older tool version has no such field, and a
    /// <c>required</c> member here would make those records undeserializable. <c>null</c> means "not recorded",
    /// which is not the same as "no operations".
    /// </remarks>
    public IReadOnlyList<string>? Descriptors { get; init; }
}
