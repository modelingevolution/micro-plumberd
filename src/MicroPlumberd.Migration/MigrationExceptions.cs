namespace MicroPlumberd.Migration;

/// <summary>
/// Thrown when an already-applied migration's code (checksum) differs from what is recorded in the store's
/// history — editing a migration that has already run is refused, because the destination cannot be
/// reproduced from a changed rule set.
/// </summary>
public sealed class MigrationChecksumMismatchException(string id, string recorded, string current)
    : Exception($"Migration '{id}' was already applied with checksum {recorded}, but its code now hashes to "
                + $"{current}. Applied migrations are immutable — add a NEW migration instead of editing this one.")
{
    /// <summary>The offending migration's Id.</summary>
    public string MigrationId { get; } = id;

    /// <summary>The checksum recorded in the store's history.</summary>
    public string RecordedChecksum { get; } = recorded;

    /// <summary>The checksum the migration's current code hashes to.</summary>
    public string CurrentChecksum { get; } = current;
}

/// <summary>Thrown when two defined migrations share the same <see cref="Migration.Id"/>.</summary>
public sealed class DuplicateMigrationIdException(string id)
    : Exception($"More than one migration declares Id '{id}'. Migration Ids must be unique.")
{
    /// <summary>The duplicated migration Id.</summary>
    public string MigrationId { get; } = id;
}
