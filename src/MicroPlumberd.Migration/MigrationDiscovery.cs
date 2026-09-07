using System.Reflection;

namespace MicroPlumberd.Migration;

/// <summary>Discovers and orders <see cref="Migration"/> implementations.</summary>
public static class MigrationDiscovery
{
    /// <summary>
    /// Finds every concrete <see cref="Migration"/> with a public parameterless constructor in the given
    /// assemblies, instantiates them, and returns them ordered by <see cref="Migration.Id"/> (ordinal).
    /// </summary>
    public static IReadOnlyList<Migration> FromAssemblies(params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        var migrations = assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => typeof(Migration).IsAssignableFrom(t) && t is { IsAbstract: false, IsClass: true })
            .Select(t => t.GetConstructor(Type.EmptyTypes) is not null
                ? (Migration)Activator.CreateInstance(t)!
                : throw new InvalidOperationException(
                    $"Migration '{t.FullName}' must have a public parameterless constructor."));

        return Order(migrations);
    }

    /// <summary>
    /// Orders migrations by <see cref="Migration.Id"/> (ordinal) and rejects duplicate ids.
    /// </summary>
    public static IReadOnlyList<Migration> Order(IEnumerable<Migration> migrations)
    {
        ArgumentNullException.ThrowIfNull(migrations);
        var ordered = migrations.OrderBy(m => m.Id, StringComparer.Ordinal).ToList();
        var duplicate = ordered.GroupBy(m => m.Id).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null) throw new DuplicateMigrationIdException(duplicate.Key);
        return ordered;
    }
}
