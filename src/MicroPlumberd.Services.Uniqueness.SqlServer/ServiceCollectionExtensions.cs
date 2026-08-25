using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MicroPlumberd.Services.Uniqueness;

/// <summary>SQL Server registration for set-wide uniqueness (the reservation pattern).</summary>
public static class SqlServerUniquenessServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IUniqueNameReservation{TCategory}"/> for a category, backed by SQL Server.
    /// </summary>
    /// <typeparam name="TCategory">The uniqueness category; names its table.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The SQL Server connection string.</param>
    /// <param name="ensureSchema">Create the table on start if absent (idempotent). Turn off when the
    /// schema is owned by migrations or the database user cannot issue DDL.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddUniquenessSqlServer<TCategory>(
        this IServiceCollection services,
        string connectionString,
        bool ensureSchema = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IUniquenessDialect, SqlServerUniquenessDialect>());
        return services.AddUniqueness<TCategory>(o => o.UseSqlServer(connectionString), ensureSchema);
    }
}
