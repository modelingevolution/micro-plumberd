using MicroPlumberd.Testing;

namespace MicroPlumberd.Services.Uniqueness.Tests;

/// <summary>
/// Owns the container-backed servers for the whole collection: ONE container per server, started lazily,
/// removed on teardown. Isolation between tests comes from a FRESH DATABASE per test inside the container
/// (see <see cref="PostgresUqProvider"/> / <see cref="SqlServerUqProvider"/>), which is indistinguishable
/// from a fresh container to anything these tests can observe, and costs a fraction of one.
/// </summary>
/// <remarks>
/// Lazy on purpose: a SQLite-only run (the default, and the whole suite minus the provider scenarios)
/// starts NO container at all. Uses the house pattern — MicroPlumberd.Testing's Docker.DotNet servers —
/// rather than a second container mechanism.
/// </remarks>
public sealed class DockerServersFixture : IAsyncLifetime
{
    readonly SemaphoreSlim _gate = new(1, 1);
    PostgresServer? _postgres;
    SqlServerServer? _sqlServer;

    public async Task<PostgresServer> Postgres()
    {
        await _gate.WaitAsync();
        try { return _postgres ??= await PostgresServer.Create().StartInDocker(); }
        finally { _gate.Release(); }
    }

    public async Task<SqlServerServer> SqlServer()
    {
        await _gate.WaitAsync();
        try { return _sqlServer ??= await SqlServerServer.Create().StartInDocker(); }
        finally { _gate.Release(); }
    }

    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>Removes every container this fixture started — on failure as well as success. xunit v2
    /// disposes fixtures through IAsyncLifetime, so this is the path that actually runs.</summary>
    public async Task DisposeAsync()
    {
        if (_postgres is not null) await _postgres.DisposeAsync();
        if (_sqlServer is not null) await _sqlServer.DisposeAsync();
    }
}

/// <summary>Test classes needing a container join this collection, so the servers are shared across
/// them and started once.</summary>
[CollectionDefinition(DockerCollection.Name)]
public class DockerCollection : ICollectionFixture<DockerServersFixture>
{
    public const string Name = "docker-servers";
}

/// <summary>Docker availability, probed once per process. Cheap (~100ms) — no container is started.</summary>
static class Docker
{
    static readonly Lazy<bool> _available = new(() =>
        PostgresServer.IsDockerAvailable().GetAwaiter().GetResult());

    public static bool Available => _available.Value;

    public const string NoDocker =
        "Docker is not available, so this scenario is NOT verified. It provisions its own container " +
        "(MicroPlumberd.Testing PostgresServer / SqlServerServer) and needs a running docker daemon.";
}

/// <summary>Marks a scenario that provisions its own PostgreSQL container. Skips LOUDLY without docker —
/// never silently passes.</summary>
public sealed class RequiresPostgresFactAttribute : FactAttribute
{
    public RequiresPostgresFactAttribute()
    {
        if (!Docker.Available) Skip = Docker.NoDocker;
    }
}

/// <summary>Marks a scenario that provisions its own SQL Server container. Skips LOUDLY without docker.</summary>
public sealed class RequiresSqlServerFactAttribute : FactAttribute
{
    public RequiresSqlServerFactAttribute()
    {
        if (!Docker.Available) Skip = Docker.NoDocker;
    }
}
