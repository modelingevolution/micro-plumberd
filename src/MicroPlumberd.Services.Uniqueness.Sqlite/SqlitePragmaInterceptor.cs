using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MicroPlumberd.Services.Uniqueness;

/// <summary>
/// Applies the pragmas the shared-volume deployment depends on to EVERY connection: a busy timeout, then
/// WAL.
/// </summary>
/// <remarks>
/// <para>
/// Why an interceptor and not the connection string: Microsoft.Data.Sqlite rejects <c>journal_mode</c> and
/// <c>busy_timeout</c> as connection-string keywords outright ("Connection string keyword 'busy_timeout'
/// is not supported"), so there is no declarative route. Its <c>Default Timeout</c> keyword is a different
/// mechanism — an ADO-level command retry, not sqlite3_busy_timeout — and would leave
/// <c>PRAGMA busy_timeout</c> reading 0.
/// </para>
/// <para>
/// Why on every OPEN and not once at startup: <c>busy_timeout</c> is PER-CONNECTION state, so a one-shot
/// pragma at boot would configure one connection and leave every later pooled connection at the default.
/// <c>journal_mode</c> is by contrast PERSISTENT in the file, so setting it per-open is a cheap no-op after
/// the first — worth it to keep one code path and to re-assert the WAL check below.
/// </para>
/// <para>
/// What <c>busy_timeout</c> does and does NOT buy, MEASURED — do not restate the folklore. It is often said
/// that at <c>busy_timeout=0</c> a writer meeting a held lock fails instantly. That is NOT true on this
/// stack: Microsoft.Data.Sqlite retries SQLITE_BUSY at the command level for <c>Default Timeout</c>
/// (default 30s), so with the shipped connection string a contended writer WAITS and succeeds even at
/// <c>busy_timeout=0</c> (measured: 3.2s against a 3s lock holder). Setting it buys three real things
/// instead: the wait happens in SQLite's own busy handler rather than the provider's 150ms poll loop; the
/// behaviour stops depending on an ADO-layer retry that is not part of SQLite's contract; and the wait
/// becomes explicit rather than inherited from an unrelated <c>Default Timeout</c> default.
/// </para>
/// <para>
/// CAVEAT worth knowing before raising it: a busy timeout LARGER than <c>Default Timeout</c> silently
/// defeats the command timeout for lock waits. The native busy handler blocks inside sqlite3_step and never
/// returns SQLITE_BUSY, so the provider's timeout check never fires — measured: <c>busy_timeout=5000</c>
/// with <c>Default Timeout=1</c> waited the full 3s and succeeded, while <c>busy_timeout=0</c> with the
/// same command timeout failed at 1.1s. Keep the busy timeout below <c>Default Timeout</c> unless that
/// override is what you want.
/// </para>
/// <para>
/// ORDER IS LOAD-BEARING: the busy timeout is set FIRST. Switching to WAL needs a brief exclusive lock, so
/// with N containers booting against one file simultaneously the WAL switch is itself contended — at the
/// default timeout of 0 it would fail instantly on whichever container lost.
/// </para>
/// </remarks>
class SqlitePragmaInterceptor(TimeSpan busyTimeout) : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        Execute(connection, $"PRAGMA busy_timeout={(int)busyTimeout.TotalMilliseconds}");
        RequireWal(Execute(connection, "PRAGMA journal_mode=WAL"));
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(connection, $"PRAGMA busy_timeout={(int)busyTimeout.TotalMilliseconds}", cancellationToken);
        RequireWal(await ExecuteAsync(connection, "PRAGMA journal_mode=WAL", cancellationToken));
    }

    static object? Execute(DbConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }

    static async Task<object?> ExecuteAsync(DbConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        return await cmd.ExecuteScalarAsync(cancellationToken);
    }

    /// <summary>
    /// Fails loudly when WAL did not take effect.
    /// </summary>
    /// <remarks>
    /// This is the point of requiring WAL. It needs shared memory (the -shm file), and shared memory needs
    /// all processes on ONE HOST — so WAL cannot be enabled over a network filesystem. SQLite reports that
    /// by leaving the journal mode alone rather than by failing, which means an unchecked WAL switch would
    /// hand back a working-looking database whose locking is unreliable across hosts. That failure mode is
    /// CORRUPTION, not merely lost uniqueness — strictly worse than the bug this library prevents — so the
    /// only safe response is to refuse to start.
    /// <para>
    /// In-memory databases report "memory" and are accepted: they are single-process by construction, so
    /// the cross-host hazard cannot arise.
    /// </para>
    /// </remarks>
    static void RequireWal(object? journalMode)
    {
        var mode = journalMode as string ?? journalMode?.ToString() ?? "(null)";
        if (string.Equals(mode, "wal", StringComparison.OrdinalIgnoreCase)) return;
        if (string.Equals(mode, "memory", StringComparison.OrdinalIgnoreCase)) return;

        throw new InvalidOperationException(
            $"Uniqueness: SQLite refused to enable WAL on this database (journal_mode is '{mode}'). The " +
            "usual cause is that the file is on a NETWORK filesystem (NFS / SMB / a cloud file share), " +
            "where WAL's shared-memory file cannot work. SQLite's locking is unreliable there and the " +
            "failure mode is DATABASE CORRUPTION, so this configuration is refused rather than run. Put " +
            "the file on a volume local to ONE host (a docker named volume or bind mount is fine, and may " +
            "be shared by any number of containers on that host), or use " +
            "MicroPlumberd.Services.Uniqueness.Postgres for a deployment that spans hosts.");
    }
}
