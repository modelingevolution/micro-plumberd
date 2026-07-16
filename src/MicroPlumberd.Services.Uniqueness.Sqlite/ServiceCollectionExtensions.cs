using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MicroPlumberd.Services.Uniqueness;

/// <summary>SQLite registration for set-wide uniqueness (the reservation pattern).</summary>
public static class SqliteUniquenessServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IUniqueNameReservation{TCategory}"/> for a category, backed by SQLite.
    /// </summary>
    /// <remarks>
    /// <b>Development, test and single-instance hosts only — NOT production.</b> SQLite is file-local and
    /// single-writer, so it cannot arbitrate across replicas: each instance would enforce uniqueness
    /// against its own file, which amounts to no uniqueness at all. Two replicas would happily accept the
    /// same name. The production default is PostgreSQL — see
    /// <c>MicroPlumberd.Services.Uniqueness.Postgres</c>.
    /// </remarks>
    /// <typeparam name="TCategory">The uniqueness category; names its table.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The SQLite connection string, e.g. <c>Data Source=uniqueness.db</c>.</param>
    /// <param name="ensureSchema">Create the table on start if absent (idempotent). Turn off when the
    /// schema is owned by migrations or the database user cannot issue DDL.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddUniquenessSqlite<TCategory>(
        this IServiceCollection services,
        string connectionString,
        bool ensureSchema = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IUniquenessDialect, SqliteUniquenessDialect>());
        return services.AddUniqueness<TCategory>(o => o.UseSqlite(connectionString), ensureSchema);
    }
}
