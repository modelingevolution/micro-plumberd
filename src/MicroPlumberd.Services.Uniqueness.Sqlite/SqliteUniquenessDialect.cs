namespace MicroPlumberd.Services.Uniqueness;

/// <summary>SQLite DDL and clock for a category's reservation table.</summary>
class SqliteUniquenessDialect : IUniquenessDialect
{
    public bool Handles(string providerName) => providerName == "Microsoft.EntityFrameworkCore.Sqlite";

    /// <summary>
    /// SQLite has no native timestamp type; EF stores <see cref="DateTime"/> as ISO-8601 TEXT, so the clock
    /// must be formatted the same way to compare and round-trip correctly. 'now' is always UTC in SQLite.
    /// </summary>
    public string UtcNowSql => "strftime('%Y-%m-%d %H:%M:%f', 'now')";

    public IEnumerable<string> CreateSchema(string table)
    {
        yield return $"""
            CREATE TABLE IF NOT EXISTS "{table}" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_{table}" PRIMARY KEY AUTOINCREMENT,
                "Name" TEXT NOT NULL,
                "SourceId" TEXT NOT NULL,
                "ValidUntil" TEXT NOT NULL,
                "IsConfirmed" INTEGER NOT NULL
            )
            """;

        // The lock the whole pattern rests on.
        yield return $"""CREATE UNIQUE INDEX IF NOT EXISTS "IX_{table}_Name" ON "{table}" ("Name")""";
        yield return $"""CREATE INDEX IF NOT EXISTS "IX_{table}_SourceId" ON "{table}" ("SourceId")""";
    }
}
