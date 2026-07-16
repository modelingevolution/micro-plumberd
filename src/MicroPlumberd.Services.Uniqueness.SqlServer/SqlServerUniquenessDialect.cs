namespace MicroPlumberd.Services.Uniqueness;

/// <summary>SQL Server DDL and clock for a category's reservation table.</summary>
/// <remarks>
/// Names are compared exactly (binary collation), matching the other providers — see CreateSchema. This is
/// applied when the table is created; a pre-existing table keeps whatever collation it already has.
/// </remarks>
class SqlServerUniquenessDialect : IUniquenessDialect
{
    public bool Handles(string providerName) => providerName == "Microsoft.EntityFrameworkCore.SqlServer";

    /// <summary>
    /// <c>SYSUTCDATETIME()</c> (not <c>GETDATE()</c>) — UTC, and datetime2-precision, matching the column.
    /// </summary>
    public string UtcNowSql => "SYSUTCDATETIME()";

    public IEnumerable<string> CreateSchema(string table)
    {
        // SQL Server has no CREATE ... IF NOT EXISTS; guard each object explicitly.
        //
        // Name is pinned to a BINARY collation on purpose. The server default (SQL_Latin1_General_CP1_CI_AS
        // and friends) is case-INsensitive, which would make the unique index case-insensitive here while
        // PostgreSQL and SQLite compare exactly — the same two names would collide on one provider and not
        // on another. Verified: without this, "ABC" is refused after "abc" on SQL Server only. The
        // documented contract is exact/ordinal comparison, so the DDL enforces it rather than inheriting
        // whatever collation the server happens to have.
        yield return $"""
            IF OBJECT_ID(N'[{table}]', N'U') IS NULL
            CREATE TABLE [{table}] (
                [Id] bigint NOT NULL IDENTITY(1,1),
                [Name] nvarchar(400) COLLATE Latin1_General_100_BIN2 NOT NULL,
                [SourceId] uniqueidentifier NOT NULL,
                [ValidUntil] datetime2 NOT NULL,
                [IsConfirmed] bit NOT NULL,
                CONSTRAINT [PK_{table}] PRIMARY KEY ([Id])
            )
            """;

        // The lock the whole pattern rests on. nvarchar(400) = 800 bytes, inside the 1700-byte key limit.
        yield return $"""
            IF NOT EXISTS (SELECT 1 FROM sys.indexes
                           WHERE name = N'IX_{table}_Name' AND object_id = OBJECT_ID(N'[{table}]'))
            CREATE UNIQUE INDEX [IX_{table}_Name] ON [{table}] ([Name])
            """;

        yield return $"""
            IF NOT EXISTS (SELECT 1 FROM sys.indexes
                           WHERE name = N'IX_{table}_SourceId' AND object_id = OBJECT_ID(N'[{table}]'))
            CREATE INDEX [IX_{table}_SourceId] ON [{table}] ([SourceId])
            """;
    }
}
