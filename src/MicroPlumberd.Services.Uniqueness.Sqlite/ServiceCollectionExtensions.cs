using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MicroPlumberd.Services.Uniqueness;

/// <summary>SQLite registration for set-wide uniqueness (the reservation pattern).</summary>
public static class SqliteUniquenessServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IUniqueNameReservation{TCategory}"/> for a category, backed by SQLite — the
    /// light, serverless option for a host whose instances share a volume. No server to run or operate.
    /// </summary>
    /// <remarks>
    /// <b>SAME HOST ONLY. Read the boundary below before deploying this.</b>
    /// <para>
    /// <b>SUPPORTED — one host.</b> A docker named volume or bind mount, shared by any number of containers
    /// on ONE machine. SQLite's POSIX advisory locks arbitrate correctly between separate processes, so N
    /// containers sharing the file get real uniqueness. Verified: 16 independent OS processes racing to
    /// insert the same value against one file left exactly one row.
    /// </para>
    /// <para>
    /// <b>PROHIBITED — across hosts.</b> NFS, SMB, or any cloud file share. SQLite's locking is unreliable
    /// on network filesystems, and the failure mode is <b>DATABASE CORRUPTION</b>, not merely lost
    /// uniqueness — strictly worse than the bug this library exists to prevent. Use
    /// <c>MicroPlumberd.Services.Uniqueness.Postgres</c> for any deployment spanning hosts.
    /// </para>
    /// <para>
    /// That boundary is enforced, not merely documented. This registration requires WAL, and WAL needs a
    /// shared-memory (-shm) file, which needs every process on one kernel — so it cannot be enabled over a
    /// network filesystem. A cross-host deployment therefore FAILS LOUDLY at startup instead of corrupting
    /// silently. (The same-host mechanism is verified; the cross-host failure is reasoned from SQLite's
    /// documented WAL constraints and has not been observed here.)
    /// </para>
    /// <para>
    /// A busy timeout is also applied to every connection, so a writer meeting a held lock waits in SQLite's
    /// own busy handler. Note this is defence in depth rather than a fix for instant failures:
    /// Microsoft.Data.Sqlite already retries a locked write at the command level for <c>Default Timeout</c>
    /// (30s by default), so contended writers wait even without it. What it buys is that the wait no longer
    /// depends on an ADO-layer retry outside SQLite's contract, and is explicit rather than inherited.
    /// </para>
    /// </remarks>
    /// <typeparam name="TCategory">The uniqueness category; names its table.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The SQLite connection string, e.g. <c>Data Source=uniqueness.db</c>.</param>
    /// <param name="ensureSchema">Create the table on start if absent (idempotent). Turn off when the
    /// schema is owned by migrations or the database user cannot issue DDL.</param>
    /// <param name="busyTimeout">How long a writer waits in SQLite's busy handler for a held lock. Default
    /// 5s; costs nothing when uncontended. Keep it BELOW the connection string's <c>Default Timeout</c>
    /// (30s unless you set it): a larger busy timeout silently defeats the command timeout for lock waits,
    /// because the native handler blocks inside SQLite and never surfaces the busy the provider would time
    /// out on.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddUniquenessSqlite<TCategory>(
        this IServiceCollection services,
        string connectionString,
        bool ensureSchema = true,
        TimeSpan? busyTimeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var timeout = busyTimeout ?? TimeSpan.FromSeconds(5);
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(busyTimeout), timeout,
                "A busy timeout of zero means a writer fails the instant it meets a held lock, which is the " +
                "default this registration exists to correct.");

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IUniquenessDialect, SqliteUniquenessDialect>());
        return services.AddUniqueness<TCategory>(
            o => o.UseSqlite(connectionString).AddInterceptors(new SqlitePragmaInterceptor(timeout)),
            ensureSchema);
    }
}
