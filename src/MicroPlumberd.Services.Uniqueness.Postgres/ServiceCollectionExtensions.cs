using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MicroPlumberd.Services.Uniqueness;

/// <summary>PostgreSQL registration for set-wide uniqueness (the reservation pattern).</summary>
public static class PostgresUniquenessServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IUniqueNameReservation{TCategory}"/> for a category, backed by PostgreSQL.
    /// This is the production default: a real multi-writer server, so the unique index arbitrates across
    /// every replica.
    /// </summary>
    /// <typeparam name="TCategory">The uniqueness category; names its table.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The Npgsql connection string.</param>
    /// <param name="ensureSchema">Create the table on start if absent (idempotent). Turn off when the
    /// schema is owned by migrations or the database user cannot issue DDL.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddUniquenessPostgres<TCategory>(
        this IServiceCollection services,
        string connectionString,
        bool ensureSchema = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IUniquenessDialect, PostgresUniquenessDialect>());
        return services.AddUniqueness<TCategory>(o => o.UseNpgsql(connectionString), ensureSchema);
    }
}
