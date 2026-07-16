using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using MicroPlumberd.Testing;

namespace MicroPlumberd.Services.Uniqueness.Tests;

/// <summary>Categories used by the tests. Names double as table names.</summary>
record CompanyNip;
record InvoiceNumber;

/// <summary>Category overriding its table name via IUniqueCategoryProvider.</summary>
record CustomNamed : IUniqueCategoryProvider
{
    public static string Category => "my_custom_table";
}

/// <summary>
/// A database provider under test. Each instance owns a throwaway database for exactly one test.
/// The provider is the ONLY place that knows how uniqueness is registered, so the suite runs
/// unchanged against SQLite and PostgreSQL (UQ-PROV-01).
/// </summary>
abstract class UqProvider : IAsyncDisposable
{
    public abstract string Name { get; }

    /// <summary>Connection string of this instance's throwaway database.</summary>
    public string ConnectionString { get; protected set; } = null!;

    /// <summary>Creates the throwaway database. Call before <see cref="Register{T}"/>.</summary>
    public abstract Task Provision();

    /// <summary>Registers uniqueness for a category through the SHIPPED public surface.</summary>
    public abstract void Register<TCategory>(IServiceCollection services, bool ensureSchema = true);

    /// <summary>Table names actually present in the database (read outside EF, on purpose: the tests
    /// assert what really landed on disk, not what EF believes it mapped).</summary>
    public abstract Task<List<string>> TableNames();

    /// <summary>
    /// Whether a UNIQUE index on Name really exists on <paramref name="table"/> — the lock the whole
    /// pattern rests on. Asserted structurally because on a single-writer provider no sequential test
    /// can observe it: the service's own re-read would mask a merely non-unique index, so a
    /// CREATE UNIQUE INDEX -> CREATE INDEX slip in a dialect would otherwise ship silently.
    /// </summary>
    public abstract Task<bool> HasUniqueIndexOnName(string table);

    /// <summary>
    /// Makes the database itself raise a REAL failure on INSERT into <paramref name="table"/>, and only
    /// on INSERT — SELECT and DELETE keep working (UQ-CONC-03). The error is emphatically NOT a
    /// unique-constraint violation: PostgreSQL reports 40P01 deadlock_detected.
    /// This is fault injection at the database, through the real driver, with the shipped registration
    /// untouched — nothing about it is simulated in the service under test.
    /// </summary>
    public abstract Task InjectInsertFault(string table);

    /// <summary>The marker carried by an injected fault, so a test can prove the failure it observed is
    /// the one it injected rather than some unrelated breakage.</summary>
    public const string FaultMarker = "uq-injected-fault";

    public abstract ValueTask DisposeAsync();

    /// <summary>
    /// Backdates a reservation's lease so it is already dead — deterministic expiry with no sleeping and
    /// no timing race. Written with raw SQL because the tests set up database state, not EF state.
    /// </summary>
    public async Task ExpireLease(string table, string name)
    {
        await using var conn = Connect();
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"UPDATE {Quote(table)} SET {Quote("ValidUntil")} = @p WHERE {Quote("Name")} = @n";
        var p = cmd.CreateParameter(); p.ParameterName = "@p"; p.Value = DateTime.UtcNow.AddHours(-1);
        var n = cmd.CreateParameter(); n.ParameterName = "@n"; n.Value = name;
        cmd.Parameters.Add(p);
        cmd.Parameters.Add(n);
        var affected = await cmd.ExecuteNonQueryAsync();
        if (affected == 0)
            throw new InvalidOperationException(
                $"ExpireLease found no row '{name}' in '{table}' — the test's premise never held.");
    }

    /// <summary>Runs raw DDL/SQL against the throwaway database.</summary>
    protected async Task Execute(params string[] statements)
    {
        await using var conn = Connect();
        await conn.OpenAsync();
        foreach (var sql in statements)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();
        }
    }

    /// <summary>Opens a raw connection to the throwaway database.</summary>
    protected abstract DbConnection Connect();

    /// <summary>Quotes an identifier for this provider.</summary>
    protected abstract string Quote(string identifier);

    /// <summary>The rows really present in a category's table, read with raw SQL rather than through
    /// EF — the tests assert the state of the database, not the state of EF's change tracker.</summary>
    public async Task<List<ResRow>> Rows(string table)
    {
        await using var conn = Connect();
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {Quote("Name")}, {Quote("SourceId")}, {Quote("ValidUntil")}, " +
                          $"{Quote("IsConfirmed")} FROM {Quote(table)}";
        var rows = new List<ResRow>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            rows.Add(new ResRow(
                r.GetString(0),
                AsGuid(r.GetValue(1)),
                AsUtc(r.GetValue(2)),
                Convert.ToBoolean(r.GetValue(3))));
        return rows;
    }

    /// <summary>Rows for one name in a category's table.</summary>
    public async Task<List<ResRow>> RowsFor(string table, string name) =>
        (await Rows(table)).Where(x => x.Name == name).ToList();

    // Providers store Guid/DateTime differently (SQLite: TEXT; PostgreSQL: uuid/timestamp).
    static Guid AsGuid(object v) => v is Guid g ? g : Guid.Parse(Convert.ToString(v)!);

    static DateTime AsUtc(object v)
    {
        var d = v is DateTime dt
            ? dt
            : DateTime.Parse(Convert.ToString(v)!, null, System.Globalization.DateTimeStyles.RoundtripKind);
        return DateTime.SpecifyKind(d, DateTimeKind.Utc);
    }
}

/// <summary>A reservation row as it exists in the database.</summary>
record ResRow(string Name, Guid SourceId, DateTime ValidUntil, bool IsConfirmed);

sealed class SqliteUqProvider : UqProvider
{
    readonly string _file = Path.Combine(Path.GetTempPath(), $"uq_{Guid.NewGuid():N}.db");

    public override string Name => "SQLite";

    public override Task Provision()
    {
        ConnectionString = $"Data Source={_file}";
        return Task.CompletedTask;
    }

    public override void Register<TCategory>(IServiceCollection services, bool ensureSchema = true) =>
        services.AddUniquenessSqlite<TCategory>(ConnectionString, ensureSchema);

    /// <summary>RAISE(ABORT) from a BEFORE INSERT trigger. SQLite reports SQLITE_CONSTRAINT_TRIGGER
    /// (1811) — a real error, and not SQLITE_CONSTRAINT_UNIQUE (2067).</summary>
    public override Task InjectInsertFault(string table) => Execute($"""
        CREATE TRIGGER "uq_fault_{table}" BEFORE INSERT ON "{table}"
        BEGIN
            SELECT RAISE(ABORT, '{FaultMarker}: database is locked');
        END
        """);

    /// <summary>
    /// Fails ONLY the UPDATE, leaving INSERT/DELETE working (UQ-ATOM-01). Lets a test interrupt Confirm
    /// exactly between the ExecuteDelete that releases the old name and the write that confirms the new
    /// one — atomicity is testable without needing a real crash.
    /// </summary>
    public Task InjectUpdateFault(string table) => Execute($"""
        CREATE TRIGGER "uq_fault_upd_{table}" BEFORE UPDATE ON "{table}"
        BEGIN
            SELECT RAISE(ABORT, '{FaultMarker}: update failed');
        END
        """);

    /// <summary>
    /// Makes <paramref name="name"/> change hands the instant an UPDATE touches it: the row is replaced by
    /// a CONFIRMED row owned by <paramref name="newOwner"/> (UQ-ATOM-03). This is the real hazard made
    /// deterministic — our lease expires and is swept, someone else takes the name, and our extension
    /// lands on a row that no longer exists.
    /// </summary>
    public Task SwapOwnerOnUpdate(string table, string name, Guid newOwner) => Execute($"""
        CREATE TRIGGER "uq_swap_{table}" BEFORE UPDATE ON "{table}"
        BEGIN
            DELETE FROM "{table}" WHERE "Name" = '{name}';
            INSERT INTO "{table}" ("Name", "SourceId", "ValidUntil", "IsConfirmed")
            VALUES ('{name}', '{newOwner}', '2999-01-01 00:00:00.000', 1);
        END
        """);

    public override async Task<List<string>> TableNames()
    {
        await using var conn = new SqliteConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
        return await ReadStrings(cmd);
    }

    public override async Task<bool> HasUniqueIndexOnName(string table)
    {
        await using var conn = new SqliteConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT il.name FROM pragma_index_list('{table}') AS il,
                                pragma_index_info(il.name) AS ii
            WHERE il."unique" = 1 AND ii.name = 'Name'
            """;
        return (await ReadStrings(cmd)).Count > 0;
    }

    protected override DbConnection Connect() => new SqliteConnection(ConnectionString);

    protected override string Quote(string identifier) => $"\"{identifier}\"";

    public override ValueTask DisposeAsync()
    {
        // ClearPool, never ClearAllPools: the latter is process-global and would rip pooled connections
        // out from under tests running in parallel in this same process.
        using (var c = new SqliteConnection(ConnectionString)) SqliteConnection.ClearPool(c);
        try { File.Delete(_file); } catch { /* temp file */ }
        return ValueTask.CompletedTask;
    }

    internal static async Task<List<string>> ReadStrings(DbCommand cmd)
    {
        var names = new List<string>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) names.Add(r.GetString(0));
        return names;
    }
}

sealed class PostgresUqProvider(PostgresServer server) : UqProvider
{
    string _db = null!;

    public override string Name => "PostgreSQL";

    public override async Task Provision()
    {
        // Fresh database per test, inside the collection's container.
        _db = $"uq_{Guid.NewGuid():N}";
        await server.CreateDatabase(_db);
        ConnectionString = server.GetConnectionString(_db);
    }

    public override void Register<TCategory>(IServiceCollection services, bool ensureSchema = true) =>
        services.AddUniquenessPostgres<TCategory>(ConnectionString, ensureSchema);

    /// <summary>A BEFORE INSERT trigger raising SQLSTATE 40P01 (deadlock_detected) — precisely the class
    /// of failure R8 says must never be misread as "lost the race". Not 23505 (unique_violation).</summary>
    public override Task InjectInsertFault(string table) => Execute(
        $"""
         CREATE OR REPLACE FUNCTION "uq_fault_{table}"() RETURNS trigger AS $$
         BEGIN
             RAISE EXCEPTION '{FaultMarker}: deadlock detected' USING ERRCODE = '40P01';
         END;
         $$ LANGUAGE plpgsql
         """,
        $"""
         CREATE TRIGGER "uq_fault_trg_{table}" BEFORE INSERT ON "{table}"
         FOR EACH ROW EXECUTE FUNCTION "uq_fault_{table}"()
         """);

    public override async Task<List<string>> TableNames()
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT tablename FROM pg_tables WHERE schemaname='public' ORDER BY tablename";
        return await SqliteUqProvider.ReadStrings(cmd);
    }

    public override async Task<bool> HasUniqueIndexOnName(string table)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT i.relname::text
            FROM pg_index x
            JOIN pg_class t ON t.oid = x.indrelid
            JOIN pg_class i ON i.oid = x.indexrelid
            JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = ANY(x.indkey)
            WHERE t.relname = @t AND x.indisunique AND a.attname = 'Name'
            """;
        var p = cmd.CreateParameter(); p.ParameterName = "@t"; p.Value = table;
        cmd.Parameters.Add(p);
        return (await SqliteUqProvider.ReadStrings(cmd)).Count > 0;
    }

    protected override DbConnection Connect() => new NpgsqlConnection(ConnectionString);

    protected override string Quote(string identifier) => $"\"{identifier}\"";

    public override async ValueTask DisposeAsync()
    {
        // ClearPool (this database's pool only), never ClearAllPools: the latter is process-global and
        // would kill the connections of tests running concurrently in this same process.
        using (var c = new NpgsqlConnection(ConnectionString)) NpgsqlConnection.ClearPool(c);
        try { await server.DropDatabase(_db); } catch { /* throwaway database */ }
    }
}

sealed class SqlServerUqProvider(SqlServerServer server) : UqProvider
{
    string _db = null!;

    public override string Name => "SqlServer";

    public override async Task Provision()
    {
        // Fresh database per test. Deliberately NOT given a collation: it inherits the server default,
        // which is case-INsensitive — the real-world condition UQ-PROV-02 must survive.
        _db = $"uq_{Guid.NewGuid():N}";
        await server.CreateDatabase(_db);
        ConnectionString = server.GetConnectionString(_db);
    }

    public override void Register<TCategory>(IServiceCollection services, bool ensureSchema = true) =>
        services.AddUniquenessSqlServer<TCategory>(ConnectionString, ensureSchema);

    /// <summary>An INSTEAD OF INSERT trigger raising a real error via THROW — not a unique violation (2627/2601).</summary>
    public override Task InjectInsertFault(string table) => Execute(
        $"""
         CREATE TRIGGER [uq_fault_{table}] ON [{table}] INSTEAD OF INSERT AS
         BEGIN
             THROW 50000, '{FaultMarker}: deadlock detected', 1;
         END
         """);

    public override async Task<List<string>> TableNames()
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sys.tables ORDER BY name";
        return await SqliteUqProvider.ReadStrings(cmd);
    }

    public override async Task<bool> HasUniqueIndexOnName(string table)
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT i.name
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = i.object_id AND c.column_id = ic.column_id
            WHERE i.object_id = OBJECT_ID(@t) AND i.is_unique = 1 AND c.name = 'Name'
            """;
        var p = cmd.CreateParameter(); p.ParameterName = "@t"; p.Value = table;
        cmd.Parameters.Add(p);
        return (await SqliteUqProvider.ReadStrings(cmd)).Count > 0;
    }

    protected override DbConnection Connect() => new SqlConnection(ConnectionString);

    protected override string Quote(string identifier) => $"[{identifier}]";

    public override async ValueTask DisposeAsync()
    {
        using (var c = new SqlConnection(ConnectionString)) SqlConnection.ClearPool(c);
        try { await server.DropDatabase(_db); } catch { /* throwaway database */ }
    }
}

/// <summary>
/// A host over a throwaway database, driven through the REAL DI registration (including the schema
/// hosted-service) so the tests exercise the shipped surface rather than internals.
/// </summary>
sealed class UqHarness : IAsyncDisposable
{
    readonly List<IHost> _hosts = new();
    public UqProvider Provider { get; private init; } = null!;

    public static async Task<UqHarness> Create(UqProvider provider)
    {
        await provider.Provision();
        return new UqHarness { Provider = provider };
    }

    /// <summary>Starts a host against the same database. Called twice by UQ-SCHEMA-01 to prove that
    /// re-running schema creation is idempotent.</summary>
    public async Task<IHost> Start(Action<IServiceCollection> configure)
    {
        var b = Host.CreateApplicationBuilder();
        b.Services.AddLogging(l => l.ClearProviders());
        configure(b.Services);
        var host = b.Build();
        await host.StartAsync();
        _hosts.Add(host);
        return host;
    }

    /// <summary>The common case: one host, one category.</summary>
    public async Task<IUniqueNameReservation<CompanyNip>> StartNip()
    {
        var host = await Start(s => Provider.Register<CompanyNip>(s));
        return host.Services.GetRequiredService<IUniqueNameReservation<CompanyNip>>();
    }

    public async Task<List<string>> TableNames() => await Provider.TableNames();

    public async ValueTask DisposeAsync()
    {
        foreach (var h in _hosts)
        {
            try { await h.StopAsync(); } catch { /* shutting down */ }
            h.Dispose();
        }
        await Provider.DisposeAsync();
    }
}
