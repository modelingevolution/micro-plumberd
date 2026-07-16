using Docker.DotNet;
using Docker.DotNet.Models;

namespace MicroPlumberd.Testing;

/// <summary>
/// A throwaway PostgreSQL server in Docker, for integration tests that need a real database engine.
/// </summary>
/// <remarks>
/// Mirrors <see cref="EventStoreServer"/>: allocate a free host port, start a container named after it so
/// parallel runs cannot collide, and remove the container on dispose.
/// <code>
/// await using var pg = await PostgresServer.Create().StartInDocker();
/// await pg.CreateDatabase("my_test");
/// var cs = pg.GetConnectionString("my_test");
/// </code>
/// <para>
/// Intended for one server per xUnit collection with a fresh database per test — starting a container is
/// far more expensive than creating a database.
/// </para>
/// </remarks>
public sealed class PostgresServer : IDisposable, IAsyncDisposable
{
    const string Image = "postgres:17-alpine";
    const string SuperUser = "postgres";
    const string SuperUserPassword = "postgres";

    /// <summary>The always-present maintenance database; connect here to create or drop others.</summary>
    public const string MaintenanceDatabase = "postgres";

    static readonly PortSearcher Searcher = new(5500);

    readonly DockerClient _client;
    readonly string? _containerName;

    /// <summary>Creates a server bound to a free host port. Nothing starts until
    /// <see cref="StartInDocker"/> is called.</summary>
    /// <param name="containerName">Overrides the generated container name. Leave null in tests: the
    /// generated name embeds the port, which is what keeps parallel runs from colliding.</param>
    /// <returns>The new server.</returns>
    public static PostgresServer Create(string? containerName = null) => new(containerName);

    /// <summary>Creates a server bound to a free host port.</summary>
    public PostgresServer() : this(null) { }

    internal PostgresServer(string? containerName)
    {
        _containerName = containerName;
        Port = Searcher.FindNextAvailablePort();
        _client = new DockerClientConfiguration().CreateClient();
    }

    /// <summary>
    /// True when a Docker daemon is reachable. Cheap — a ping, never starts a container.
    /// </summary>
    /// <remarks>Call this to decide whether to SKIP a test when Docker is absent. A test that needs Docker
    /// must skip loudly rather than pass by accident, and must not pay a start-up timeout to discover it.</remarks>
    /// <param name="timeout">How long to wait for the ping. Default 2s.</param>
    /// <param name="cancellationToken">Cancels the probe.</param>
    /// <returns>True if Docker answered.</returns>
    public static Task<bool> IsDockerAvailable(TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
        DockerServerSupport.IsDockerAvailable(timeout ?? TimeSpan.FromSeconds(2), cancellationToken);

    /// <summary>The host port the server is published on. May change if a port turns out to be taken.</summary>
    public int Port { get; private set; }

    /// <summary>The container's name. Derived from the port unless one was supplied.</summary>
    public string ContainerName => _containerName ?? $"mp-postgres-{Port}";

    /// <summary>Connection string for the <see cref="MaintenanceDatabase"/>.</summary>
    public string ConnectionString => GetConnectionString(MaintenanceDatabase);

    /// <summary>Connection string for a named database on this server.</summary>
    /// <param name="database">The database name.</param>
    /// <returns>An Npgsql connection string.</returns>
    public string GetConnectionString(string database) =>
        $"Host=127.0.0.1;Port={Port};Database={database};Username={SuperUser};Password={SuperUserPassword}";

    /// <summary>
    /// Pulls the image if needed, starts the container, and (by default) waits until the server actually
    /// answers queries.
    /// </summary>
    /// <param name="wait">Wait for readiness. Passing false returns as soon as the container is started,
    /// before the server can serve — you then own the race.</param>
    /// <param name="timeout">How long to wait for readiness. Default 60s.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>This instance, so the call can be chained onto <see cref="Create"/>.</returns>
    public async Task<PostgresServer> StartInDocker(bool wait = true, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        // Pull BEFORE the readiness clock starts: a cold pull is a pull problem, not a readiness problem,
        // and charging it to the readiness budget misdiagnoses it.
        await DockerServerSupport.EnsureImage(_client, Image, cancellationToken);

        for (var attempt = 1; ; attempt++)
        {
            // Always a FRESH container: adopting one left by a previous run silently inherits its settings
            // and its data.
            await DockerServerSupport.RemoveStaleContainer(_client, ContainerName, cancellationToken);

            var created = await _client.Containers.CreateContainerAsync(new CreateContainerParameters
            {
                Image = Image,
                Name = ContainerName,
                Env =
                [
                    $"POSTGRES_USER={SuperUser}",
                    $"POSTGRES_PASSWORD={SuperUserPassword}",
                    $"POSTGRES_DB={MaintenanceDatabase}"
                ],
                ExposedPorts = new Dictionary<string, EmptyStruct> { { "5432", default } },
                HostConfig = new HostConfig
                {
                    PortBindings = new Dictionary<string, IList<PortBinding>>
                    {
                        { "5432", new List<PortBinding> { new() { HostPort = $"{Port}", HostIP = "0.0.0.0" } } }
                    }
                }
            }, cancellationToken);

            try
            {
                await _client.Containers.StartContainerAsync(created.ID, new ContainerStartParameters(),
                    cancellationToken);
                break;
            }
            catch (Exception ex) when (DockerServerSupport.IsPortUnavailable(ex) && attempt < PortAttempts)
            {
                // Normal under WSL2 mirrored networking (the port can be held on the Windows side) and
                // because PortSearcher is TOCTOU. Take the next port rather than surfacing it.
                await DockerServerSupport.Cleanup(_client, ContainerName);
                Port = Searcher.FindNextAvailablePort();
            }
        }

        if (wait)
            await DockerServerSupport.WaitUntilReady(
                Probe,
                ct => DockerServerSupport.AssertContainerAlive(_client, ContainerName, ct),
                timeout ?? TimeSpan.FromSeconds(60),
                $"PostgreSQL container '{ContainerName}'",
                cancellationToken);

        return this;
    }

    /// <summary>How many host ports to try before giving up.</summary>
    const int PortAttempts = 5;

    /// <summary>
    /// True once a real query succeeds over TCP.
    /// </summary>
    /// <remarks>
    /// Probing over TCP (-h 127.0.0.1) rather than the unix socket is deliberate. The official image runs a
    /// TEMPORARY server during initdb that listens on the socket only; a socket probe can succeed against
    /// that one and report ready before the real server is up, which is a race the caller then loses.
    /// </remarks>
    async Task<(bool, string)> Probe(CancellationToken cancellationToken)
    {
        var container = await DockerServerSupport.FindContainer(_client, ContainerName, cancellationToken);
        if (container == null) return (false, "container not found");

        var (exitCode, stdout, stderr) = await DockerServerSupport.Exec(_client, container.ID,
            ["psql", "-h", "127.0.0.1", "-U", SuperUser, "-d", MaintenanceDatabase, "-tAc", "SELECT 1"],
            cancellationToken);

        return exitCode == 0 && stdout.Contains('1')
            ? (true, "")
            : (false, $"psql exit {exitCode}: {Summarise(stderr, stdout)}");
    }

    /// <summary>Creates a database on this server.</summary>
    /// <param name="database">Name; ASCII letters, digits and underscore only.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    public Task CreateDatabase(string database, CancellationToken cancellationToken = default) =>
        Sql($"CREATE DATABASE \"{DockerServerSupport.ValidateDatabaseName(database)}\"", cancellationToken);

    /// <summary>Drops a database, disconnecting anything still attached. No-op if it does not exist.</summary>
    /// <param name="database">Name; ASCII letters, digits and underscore only.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    public Task DropDatabase(string database, CancellationToken cancellationToken = default) =>
        Sql($"DROP DATABASE IF EXISTS \"{DockerServerSupport.ValidateDatabaseName(database)}\" WITH (FORCE)",
            cancellationToken);

    async Task Sql(string sql, CancellationToken cancellationToken)
    {
        var container = await DockerServerSupport.FindContainer(_client, ContainerName, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Container '{ContainerName}' is not present. Call {nameof(StartInDocker)} first.");

        var (exitCode, stdout, stderr) = await DockerServerSupport.Exec(_client, container.ID,
            ["psql", "-h", "127.0.0.1", "-U", SuperUser, "-d", MaintenanceDatabase, "-v", "ON_ERROR_STOP=1",
                "-c", sql], cancellationToken);

        if (exitCode != 0)
            throw new InvalidOperationException($"psql failed ({exitCode}) running: {sql}\n{Summarise(stderr, stdout)}");
    }

    static string Summarise(string stderr, string stdout)
    {
        var text = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
        return text.Trim();
    }

    /// <summary>
    /// Stops and removes the container. PREFER <see cref="DisposeAsync"/> (<c>await using</c>).
    /// </summary>
    /// <remarks>
    /// Kept as a fallback because xunit v2 disposes fixtures via IDisposable/IAsyncLifetime and does not
    /// honour IAsyncDisposable — dropping this would silently leak a container per fixture. The work is
    /// pushed to the thread pool before blocking, which is what makes it safe: the continuation never needs
    /// the caller's synchronization context, so it cannot deadlock the way a bare <c>.Result</c> would.
    /// </remarks>
    public void Dispose() =>
        Task.Run(() => DockerServerSupport.Cleanup(_client, ContainerName)).GetAwaiter().GetResult();

    /// <summary>Stops and removes the container. The preferred disposal path.</summary>
    public async ValueTask DisposeAsync() => await DockerServerSupport.Cleanup(_client, ContainerName);
}
