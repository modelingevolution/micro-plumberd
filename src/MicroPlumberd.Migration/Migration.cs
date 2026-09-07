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

    /// <summary>
    /// Optional checksum that REPLACES the default one computed over this migration's compiled operation
    /// descriptors. <c>null</c> (the default) keeps the descriptor checksum, which is right for every
    /// hand-written migration.
    /// </summary>
    /// <remarks>
    /// <para>Override it only when the rules are defined by an EXTERNAL ARTIFACT rather than by this class's
    /// code — a rewrite script, for instance. There, the descriptor checksum is not merely weaker, it is
    /// WRONG: every script compiles to the same host delegates, so two completely different scripts produce
    /// identical descriptors (same method identity, same IL) and therefore the same checksum. The history
    /// guard would then accept a store rewritten by a different script as "already applied". The artifact's
    /// own hash — <c>sha256(script text)</c> — is the only thing that identifies those rules.</para>
    /// <para>The value is recorded verbatim in <see cref="MigrationApplied.Checksum"/> and is what the
    /// re-run guard compares, so it must change whenever the rules change.</para>
    /// </remarks>
    public virtual string? ChecksumOverride => null;

    /// <summary>Declares this migration's rewrite rules against <paramref name="b"/>.</summary>
    public abstract void Migrate(IMigrationBuilder b);
}
