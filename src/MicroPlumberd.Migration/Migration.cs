namespace MicroPlumberd.Migration;

/// <summary>
/// A single, ordered, RAW event-store migration. A migration has a stable <see cref="Id"/> and declares
/// its rewrite rules in <see cref="Migrate"/>. There is intentionally no <c>Up</c>/<c>Down</c> — the copy
/// engine rewrites SOURCE into a fresh DEST, so a migration is forward-only.
/// </summary>
/// <remarks>
/// <see cref="Id"/> must be unique and sortable — use a zero-padded numeric prefix, e.g.
/// <c>0001_drop_tombstoned_test_offers</c>. Migrations run in ascending ordinal order of <see cref="Id"/>.
/// Implementations must have a public parameterless constructor so they can be discovered and instantiated.
/// </remarks>
public abstract class Migration
{
    /// <summary>Stable, unique, sortable identifier (e.g. <c>0001_drop_tombstoned_test_offers</c>).</summary>
    public abstract string Id { get; }

    /// <summary>
    /// Optional human-readable name recorded in history. Defaults to <see cref="Id"/>.
    /// </summary>
    public virtual string Name => Id;

    /// <summary>Declares this migration's rewrite rules against <paramref name="b"/>.</summary>
    public abstract void Migrate(IMigrationBuilder b);
}
