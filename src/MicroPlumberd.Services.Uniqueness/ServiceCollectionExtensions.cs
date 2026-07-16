using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MicroPlumberd.Services.Uniqueness;

/// <summary>Registration for set-wide uniqueness (the reservation pattern).</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IUniqueNameReservation{TCategory}"/> for a category. Each category gets its
    /// own table, named after <typeparamref name="TCategory"/> (or its
    /// <see cref="IUniqueCategoryProvider.Category"/> when implemented); several categories may share
    /// one database.
    /// </summary>
    /// <remarks>
    /// <b>A matching <see cref="IUniquenessDialect"/> is MANDATORY</b> — install
    /// MicroPlumberd.Services.Uniqueness.Sqlite, .Postgres or .SqlServer and use their AddUniquenessXxx
    /// overloads, or register a dialect yourself. It supplies both the schema DDL and the database-clock
    /// expression every lease is measured against, so it is required even when
    /// <paramref name="ensureSchema"/> is false. Without one, registration is accepted but the first
    /// reservation throws.
    /// <para>
    /// This is deliberate, and it is a safety feature rather than a limitation. Uniqueness rests entirely
    /// on the database enforcing a UNIQUE index; a provider that does not enforce one (EF Core's InMemory
    /// provider does not) would yield SILENT ZERO uniqueness — protection that looks present and is not.
    /// Requiring a dialect makes that configuration structurally impossible to reach by accident.
    /// </para>
    /// </remarks>
    /// <typeparam name="TCategory">The uniqueness category; names its table.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Provider selection, e.g. <c>o =&gt; o.UseNpgsql(cs)</c>.</param>
    /// <param name="ensureSchema">Create the table on start if absent (idempotent). Turn off when the
    /// schema is owned by migrations or the database user cannot issue DDL.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddUniqueness<TCategory>(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configure,
        bool ensureSchema = true)
    {
        ArgumentNullException.ThrowIfNull(configure);

        services.AddLogging();
        services.AddDbContextFactory<UniquenessDb<TCategory>>(configure);
        services.AddSingleton<IUniqueNameReservation<TCategory>, UniqueNameReservationService<TCategory>>();

        if (ensureSchema)
            services.AddHostedService<UniquenessSchemaInitializer<TCategory>>();

        return services;
    }
}

/// <summary>Creates the category's table before the app starts serving.</summary>
class UniquenessSchemaInitializer<TCategory>(
    IDbContextFactory<UniquenessDb<TCategory>> factory,
    IEnumerable<IUniquenessDialect> dialects) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var table = CategoryName<TCategory>.Value;
        if (table.AsSpan().IndexOfAny("\"[]`;'") >= 0)
            throw new InvalidOperationException(
                $"Uniqueness category '{table}' contains characters that cannot appear in a table name.");

        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var provider = db.Database.ProviderName ?? "";
        var dialect = dialects.FirstOrDefault(d => d.Handles(provider))
            ?? throw new InvalidOperationException(
                $"No {nameof(IUniquenessDialect)} is registered for EF provider '{provider}'. Install " +
                "MicroPlumberd.Services.Uniqueness.Sqlite, .Postgres or .SqlServer (and use its " +
                "AddUniquenessXxx overload), register your own dialect, or pass ensureSchema: false " +
                "and create the table yourself.");

        foreach (var sql in dialect.CreateSchema(table))
            await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
