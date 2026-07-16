using Docker.DotNet;
using Docker.DotNet.Models;

namespace MicroPlumberd.Testing;

/// <summary>
/// A throwaway SQL Server in Docker, for integration tests that need a real database engine.
/// </summary>
/// <remarks>
/// Mirrors <see cref="EventStoreServer"/>: allocate a free host port, start a container named after it so
/// parallel runs cannot collide, and remove the container on dispose.
/// <code>
/// await using var ms = await SqlServerServer.Create().StartInDocker();
/// await ms.CreateDatabase("my_test");
/// var cs = ms.GetConnectionString("my_test");
/// </code>
/// <para>
/// Expect ~20-30s to first readiness — considerably slower than <see cref="PostgresServer"/>. Use one
/// server per xUnit collection with a fresh database per test.
/// </para>
/// <para>
/// The server keeps its DEFAULT collation, which is case-insensitive. That is deliberate — see
/// <see cref="StartInDocker"/>.
/// </para>
/// </remarks>
public sealed class SqlServerServer : IDisposable, IAsyncDisposable
{
    const string Image = "mcr.microsoft.com/mssql/server:2022-latest";
    const string SaUser = "sa";

    /// <summary>The sa password. Meets SQL Server's complexity policy; fixed so tests are reproducible.</summary>
    public const string SaPassword = "MicroPlumberd!Test1";

    /// <summary>The always-present maintenance database; connect here to create or drop others.</summary>
    public const string MaintenanceDatabase = "master";

    static readonly PortSearcher Searcher = new(14330);

    readonly DockerClient _client;
    readonly string? _containerName;

    /// <summary>Creates a server bound to a free host port. Nothing starts until
    /// <see cref="StartInDocker"/> is called.</summary>
    /// <param name="containerName">Overrides the generated container name. Leave null in tests: the
    /// generated name embeds the port, which is what keeps parallel runs from colliding.</param>
    /// <returns>The new server.</returns>
    public static SqlServerServer Create(string? containerName = null) => new(containerName);

    /// <summary>Creates a server bound to a free host port.</summary>
    public SqlServerServer() : this(null) { }

    internal SqlServerServer(string? containerName)
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

    /// <summary>How many host ports to try before giving up.</summary>
    const int PortAttempts = 5;

    /// <summary>The container's name. Derived from the port unless one was supplied.</summary>
    public string ContainerName => _containerName ?? $"mp-sqlserver-{Port}";

    /// <summary>Connection string for the <see cref="MaintenanceDatabase"/>.</summary>
    public string ConnectionString => GetConnectionString(MaintenanceDatabase);

    /// <summary>Connection string for a named database on this server.</summary>
    /// <param name="database">The database name.</param>
    /// <returns>A Microsoft.Data.SqlClient connection string.</returns>
    public string GetConnectionString(string database) =>
        $"Server=127.0.0.1,{Port};Database={database};User Id={SaUser};Password={SaPassword};TrustServerCertificate=true";

    /// <summary>
    /// Pulls the image if needed, starts the container, and (by default) waits until the server actually
    /// answers queries.
    /// </summary>
    /// <remarks>
    /// The server's collation is left at the image DEFAULT (SQL_Latin1_General_CP1_CI_AS, i.e.
    /// case-INsensitive). Do not "helpfully" set MSSQL_COLLATION to a binary collation here: a
    /// case-insensitive default is precisely the condition tests need to run against, and forcing it at the
    /// server would make those tests pass for the wrong reason and prove nothing.
    /// </remarks>
    /// <param name="wait">Wait for readiness. Passing false returns as soon as the container is started,
    /// before the server can serve — you then own the race.</param>
    /// <param name="timeout">How long to wait for readiness. Default 120s; SQL Server is slow to start.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>This instance, so the call can be chained onto <see cref="Create"/>.</returns>
    public async Task<SqlServerServer> StartInDocker(bool wait = true, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        // Pull BEFORE the readiness clock starts. This image is ~1.5GB cold and would blow any sane
        // readiness budget on a clean CI runner — a pull problem misreported as a readiness problem.
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
                    "ACCEPT_EULA=Y",
                    $"MSSQL_SA_PASSWORD={SaPassword}",
                    "MSSQL_PID=Developer"
                    // MSSQL_COLLATION deliberately unset — see the remarks above.
                ],
                ExposedPorts = new Dictionary<string, EmptyStruct> { { "1433", default } },
                HostConfig = new HostConfig
                {
                    PortBindings = new Dictionary<string, IList<PortBinding>>
                    {
                        { "1433", new List<PortBinding> { new() { HostPort = $"{Port}", HostIP = "0.0.0.0" } } }
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
            // AssertContainerAlive matters most here: mssql EXITS silently if MSSQL_SA_PASSWORD fails its
            // complexity policy, and without this the wait would blame readiness for a dead container.
            await DockerServerSupport.WaitUntilReady(
                Probe,
                ct => DockerServerSupport.AssertContainerAlive(_client, ContainerName, ct),
                timeout ?? TimeSpan.FromSeconds(120),
                $"SQL Server container '{ContainerName}'",
                cancellationToken);

        return this;
    }

    async Task<(bool, string)> Probe(CancellationToken cancellationToken)
    {
        var (exitCode, stdout, stderr) = await RunSqlcmd("SELECT 1", cancellationToken);
        return exitCode == 0 ? (true, "") : (false, $"sqlcmd exit {exitCode}: {Summarise(stderr, stdout)}");
    }

    /// <summary>Creates a database on this server.</summary>
    /// <param name="database">Name; ASCII letters, digits and underscore only.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    public Task CreateDatabase(string database, CancellationToken cancellationToken = default) =>
        Sql($"CREATE DATABASE [{DockerServerSupport.ValidateDatabaseName(database)}]", cancellationToken);

    /// <summary>Drops a database, disconnecting anything still attached. No-op if it does not exist.</summary>
    /// <param name="database">Name; ASCII letters, digits and underscore only.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    public Task DropDatabase(string database, CancellationToken cancellationToken = default)
    {
        var name = DockerServerSupport.ValidateDatabaseName(database);
        return Sql($"""
            IF DB_ID(N'{name}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{name}];
            END
            """, cancellationToken);
    }

    async Task Sql(string sql, CancellationToken cancellationToken)
    {
        var (exitCode, stdout, stderr) = await RunSqlcmd(sql, cancellationToken);
        if (exitCode != 0)
            throw new InvalidOperationException(
                $"sqlcmd failed ({exitCode}) running: {sql}\n{Summarise(stderr, stdout)}");
    }

    /// <summary>
    /// Runs a statement through sqlcmd inside the container — no SqlClient dependency needed.
    /// </summary>
    /// <remarks>
    /// The command is passed as an argv array, never through a shell. Going through <c>sh -c</c> would mean
    /// quoting the statement, and any SQL containing a quote — <c>DB_ID(N'x')</c>, a string literal — would
    /// be silently mangled by the shell rather than rejected. sqlcmd's <c>-b</c> makes it return non-zero
    /// on a SQL error, which is what makes this a real check rather than a TCP ping.
    /// </remarks>
    async Task<(long ExitCode, string Stdout, string Stderr)> RunSqlcmd(string sql, CancellationToken cancellationToken)
    {
        var container = await DockerServerSupport.FindContainer(_client, ContainerName, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Container '{ContainerName}' is not present. Call {nameof(StartInDocker)} first.");

        var (path, trustCert) = await ResolveSqlcmd(container.ID, cancellationToken);

        List<string> command = [path, "-S", "localhost", "-U", SaUser, "-P", SaPassword, "-b", "-Q", sql];
        if (trustCert) command.Insert(1, "-C");

        return await DockerServerSupport.Exec(_client, container.ID, command, cancellationToken);
    }

    string? _sqlcmdPath;
    bool _sqlcmdNeedsTrust;

    /// <summary>Locates sqlcmd inside the image, once.</summary>
    /// <remarks>2022 images ship mssql-tools18, whose sqlcmd verifies the server certificate by default and
    /// so needs -C; older images ship mssql-tools, whose sqlcmd rejects -C. Probing rather than assuming
    /// means a change of image tag cannot silently break every call.</remarks>
    async Task<(string Path, bool TrustCert)> ResolveSqlcmd(string containerId, CancellationToken cancellationToken)
    {
        if (_sqlcmdPath != null) return (_sqlcmdPath, _sqlcmdNeedsTrust);

        foreach (var (path, trust) in new[]
                 {
                     ("/opt/mssql-tools18/bin/sqlcmd", true),
                     ("/opt/mssql-tools/bin/sqlcmd", false)
                 })
        {
            var (exitCode, _, _) = await DockerServerSupport.Exec(_client, containerId,
                ["test", "-x", path], cancellationToken);
            if (exitCode != 0) continue;

            _sqlcmdPath = path;
            _sqlcmdNeedsTrust = trust;
            return (path, trust);
        }

        throw new InvalidOperationException(
            $"sqlcmd was not found in container '{ContainerName}' (looked in /opt/mssql-tools18 and " +
            "/opt/mssql-tools). The image may have changed its tooling layout.");
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
