namespace MicroPlumberd.Services.Uniqueness;

/// <summary>
/// Provider-specific SQL for a category's reservation table: the DDL that creates it, and the
/// expression that reads the database clock. Supplied by the provider packages
/// (MicroPlumberd.Services.Uniqueness.Sqlite / .Postgres / .SqlServer); implement your own to
/// support another database.
/// </summary>
public interface IUniquenessDialect
{
    /// <summary>True when this dialect handles the given EF Core provider (see DatabaseFacade.ProviderName).</summary>
    bool Handles(string providerName);

    /// <summary>
    /// A scalar SQL expression yielding the DATABASE's current UTC time — e.g. <c>now()</c>,
    /// <c>SYSUTCDATETIME()</c>. It MUST yield UTC: the value is read as a <see cref="DateTime"/> and its
    /// <see cref="DateTime.Kind"/> forced to <see cref="DateTimeKind.Utc"/> without conversion.
    /// </summary>
    /// <remarks>
    /// Every lease is evaluated against this, never against the app server's clock. The rows are shared
    /// by all instances, so an app-server clock would let skew between instances directly shorten or
    /// lengthen leases — one host running fast would expire another's live reservation. The database is
    /// the one clock all instances already agree on.
    /// </remarks>
    string UtcNowSql { get; }

    /// <summary>
    /// Idempotent statements creating <paramref name="table"/> and its indexes. Runs on every start,
    /// so every statement must tolerate the table already existing.
    /// </summary>
    /// <remarks>
    /// The table needs: Id (autoincrement PK), Name (string, max 400, NOT NULL), SourceId (guid, NOT NULL),
    /// ValidUntil (UTC timestamp, NOT NULL), IsConfirmed (bool, NOT NULL); a UNIQUE index "IX_{table}_Name"
    /// on Name — this is the lock the whole pattern rests on — and a non-unique "IX_{table}_SourceId".
    /// </remarks>
    IEnumerable<string> CreateSchema(string table);
}
