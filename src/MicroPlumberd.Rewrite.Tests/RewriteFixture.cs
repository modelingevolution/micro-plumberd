using System.Text;
using System.Text.Json;
using Docker.DotNet;
using Docker.DotNet.Models;
using FluentAssertions;
using KurrentDB.Client;
using MicroPlumberd.Rewrite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace MicroPlumberd.Rewrite.Tests;

/// <summary>
/// The end-to-end fixture: a REAL KurrentDB container whose data directory is a bind-mounted temp directory,
/// seeded exactly as `iteration-1/test-scenarios.md` describes, with the dangling link the pure-copy scenario
/// depends on.
/// </summary>
/// <remarks>
/// <para><b>The fixture proves its own precondition.</b> A scenario about repairing a dangling link is
/// worthless if the fixture never produced one, and "the rewritten store has no dangling links" passes
/// trivially against a source that had none. <see cref="AssertDanglingLinkExistsAsync"/> is called before every
/// such scenario and fails the test if the setup did not take.</para>
/// <para>The container is labelled <c>com.docker.compose.project=mp-rewrite-test</c> so the sibling guard has
/// something real to find, and named <c>mp-rewrite-test-*</c> so a leftover is unmistakably ours and can be
/// removed by exact name.</para>
/// </remarks>
public sealed class RewriteFixture : IAsyncDisposable
{
    /// <summary>
    /// The compose project this fixture's containers are labelled with. Per fixture rather than shared, so a
    /// container left behind by a crashed run cannot make the next scenario refuse for someone else's reason.
    /// </summary>
    public string ComposeProject { get; }

    /// <summary>The event id `Cmd-1`'s only event carries — E2E-04 matches on it.</summary>
    public static readonly Uuid CommandEventId = Uuid.FromGuid(Guid.Parse("c1ceefa2-363b-44ce-93a8-c54a0517550f"));

    /// <summary>The merge stream AND the projection name — MicroPlumberd names a join projection after its output.</summary>
    public const string MergeStream = ">Test";

    /// <summary>The stream whose events expire, leaving `$et-*` holding links that no longer resolve.</summary>
    public const string ExpiringStream = "Expiring-1";

    /// <summary>
    /// A join projection written WITHOUT MicroPlumberd's null guard — the shape that faults on a dangling
    /// link, which is the production incident this tool exists for (a command-router projection Faulted on the
    /// AMZ neuron, four hours over VPN).
    /// </summary>
    public const string FaultyProjection = ">Faulty";

    private readonly DockerClient _docker;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ITestOutputHelper? _output;
    private readonly List<string> _extraContainers = [];

    private RewriteFixture(DockerClient docker, ILoggerFactory lf, ITestOutputHelper? output, string rootDir,
        string storeDir, string containerId, string containerName, int port, string composeProject)
    {
        ComposeProject = composeProject;
        _docker = docker;
        _loggerFactory = lf;
        _output = output;
        RootDir = rootDir;
        StoreDir = storeDir;
        ContainerId = containerId;
        ContainerName = containerName;
        Port = port;
    }

    /// <summary>The bind-mount parent — `P` in the scenarios. Backups and the new store appear here.</summary>
    public string RootDir { get; }

    /// <summary>`P/data` — the live store directory.</summary>
    public string StoreDir { get; }

    /// <summary>Docker id of the store container under test.</summary>
    public string ContainerId { get; }

    /// <summary>Its name.</summary>
    public string ContainerName { get; }

    /// <summary>The host port 2113 is published on (loopback only).</summary>
    public int Port { get; }

    /// <summary>Connection string for the container under test.</summary>
    public string ConnectionString => $"esdb://admin:changeit@127.0.0.1:{Port}?tls=false&tlsVerifyCert=false";

    /// <summary>The image under test — override with <c>MP_REWRITE_TEST_IMAGE</c>.</summary>
    public static string Image =>
        Environment.GetEnvironmentVariable("MP_REWRITE_TEST_IMAGE")
        ?? "docker.kurrent.io/kurrent-latest/kurrentdb:latest";

    /// <summary>Starts a container, seeds it, and proves the dangling-link precondition holds.</summary>
    public static async Task<RewriteFixture> StartAsync(ITestOutputHelper? output = null)
    {
        var docker = new DockerClientConfiguration().CreateClient();
        var lf = output is null
            ? (ILoggerFactory)NullLoggerFactory.Instance
            : LoggerFactory.Create(b => b.AddProvider(new TestOutputLoggerProvider(output)).SetMinimumLevel(LogLevel.Information));

        var tag = Guid.NewGuid().ToString("N")[..10];
        var root = Path.Combine(Path.GetTempPath(), $"mp-rewrite-test-{tag}");
        var store = Path.Combine(root, "data");
        Directory.CreateDirectory(store);
        // The image runs as uid 1001 and the test process does not; the store directory must be writable by
        // the container's user or KurrentDB never starts.
        DirectorySwap.MakeContainerWritable(root);
        DirectorySwap.MakeContainerWritable(store);

        var name = $"mp-rewrite-test-{tag}";
        var composeProject = $"mp-rewrite-test-{tag}";
        var port = FreePort();
        var created = await docker.Containers.CreateContainerAsync(new CreateContainerParameters
        {
            Image = Image,
            Name = name,
            Labels = new Dictionary<string, string> { [DockerStore.ComposeProjectLabel] = composeProject },
            Env =
            [
                "KURRENTDB_RUN_PROJECTIONS=All",
                "KURRENTDB_START_STANDARD_PROJECTIONS=true",
                "KURRENTDB_INSECURE=true",
                "KURRENTDB_ENABLE_ATOM_PUB_OVER_HTTP=true",
                "KURRENTDB_MEM_DB=false"
            ],
            ExposedPorts = new Dictionary<string, EmptyStruct> { ["2113"] = default },
            HostConfig = new HostConfig
            {
                // Default bridge only, published to loopback: this host's docker pool overlaps the company VPN,
                // so a test must never create a network.
                Binds = [$"{store}:/var/lib/kurrentdb"],
                PortBindings = new Dictionary<string, IList<PortBinding>>
                {
                    ["2113"] = [new PortBinding { HostIP = "127.0.0.1", HostPort = port.ToString() }]
                }
            }
        });
        await docker.Containers.StartContainerAsync(created.ID, new ContainerStartParameters());

        var fixture = new RewriteFixture(docker, lf, output, root, store, created.ID, name, port, composeProject);
        await StoreHealth.WaitLiveAsync(fixture.ConnectionString, TimeSpan.FromSeconds(120),
            lf.CreateLogger("fixture"));
        await fixture.SeedAsync();
        return fixture;
    }

    // ---------------------------------------------------------------- seeding

    private async Task SeedAsync()
    {
        var settings = KurrentDBClientSettings.Create(ConnectionString);
        await using var client = new KurrentDBClient(settings);
        await using var projections = new KurrentDBProjectionManagementClient(settings);

        await Append(client, "Order-1", StreamState.NoStream, ("OrderCreated", """{"Customer":"acme","Total":10}"""));
        await Append(client, "Order-1", 0ul, ("LineAdded", """{"Sku":"a","Qty":1}"""));
        await Append(client, "Order-1", 1ul, ("LineAdded", """{"Sku":"b","Qty":2}"""));

        await Append(client, "Order-2", StreamState.NoStream, ("OrderCreated", """{"Customer":"beta","Total":20}"""));
        await Append(client, "Order-2", 0ul, ("LineAdded", """{"Sku":"c","Qty":3}"""));

        await Append(client, "Junk-1", StreamState.NoStream, ("Noise", """{"n":1}"""));
        await Append(client, "Junk-1", 0ul, ("Noise", """{"n":2}"""));

        await client.AppendToStreamAsync("Cmd-1", StreamState.NoStream,
        [
            new EventData(CommandEventId, "StopPipelineCommand", Encoding.UTF8.GetBytes("""{"Reason":"manual"}"""),
                Encoding.UTF8.GetBytes(
                    """{"$correlationId":"11111111-1111-1111-1111-111111111111","$causationId":"22222222-2222-2222-2222-222222222222"}"""))
        ]);

        // The dangling link: LineAdded events in a stream that then expires. $by_event_type has already linked
        // them into $et-LineAdded, and those links outlive their targets.
        await Append(client, ExpiringStream, StreamState.NoStream, ("LineAdded", """{"Sku":"ghost","Qty":9}"""));
        await Append(client, ExpiringStream, 0ul, ("LineAdded", """{"Sku":"ghost2","Qty":8}"""));
        await Append(client, ExpiringStream, 1ul, ("OrderCreated", """{"Customer":"ghost","Total":0}"""));

        await WaitForLinkCountAsync(client, "$et-LineAdded", 4, TimeSpan.FromSeconds(60));
        await WaitForLinkCountAsync(client, "$et-OrderCreated", 3, TimeSpan.FromSeconds(60));

        // Expire them AFTER they are indexed, so the links exist and the targets stop resolving.
        await client.SetStreamMetadataAsync(ExpiringStream, StreamState.Any,
            new KurrentDB.Client.StreamMetadata(maxAge: TimeSpan.FromSeconds(1)));

        // The app's join projection: named after its output stream, exactly as MicroPlumberd creates it.
        var query = $"fromStreams(['$et-OrderCreated']).when({{ $any: function(s,e){{ if(e && e.streamId !== null "
                    + $"&& e.sequenceNumber >= 0) linkTo('{MergeStream}', e) }} }});";
        await projections.CreateContinuousAsync(MergeStream, query, trackEmittedStreams: true);
        await projections.DisableAsync(MergeStream);
        await projections.UpdateAsync(MergeStream, query, emitEnabled: true);
        await projections.EnableAsync(MergeStream);
        await WaitForLinkCountAsync(client, MergeStream, 2, TimeSpan.FromSeconds(60));

        await WaitUntilDanglingAsync(client, TimeSpan.FromSeconds(30));

        // Created LAST, i.e. after the targets have expired, so its catch-up meets the dangling link. Its
        // query has no `e && e.streamId !== null` guard, which is exactly how a real router projection ends up
        // Faulted rather than merely wrong.
        var faultyQuery = $"fromStreams(['$et-OrderCreated']).when({{ $any: function(s,e){{ "
                          + $"linkTo('{FaultyProjection}', e) }} }});";
        await projections.CreateContinuousAsync(FaultyProjection, faultyQuery, trackEmittedStreams: true);
        await projections.DisableAsync(FaultyProjection);
        await projections.UpdateAsync(FaultyProjection, faultyQuery, emitEnabled: true);
        await projections.EnableAsync(FaultyProjection);
    }

    private static Task Append(KurrentDBClient c, string stream, StreamState expected, (string Type, string Json) e) =>
        c.AppendToStreamAsync(stream, expected,
            [new EventData(Uuid.NewUuid(), e.Type, Encoding.UTF8.GetBytes(e.Json), Encoding.UTF8.GetBytes("{}"))]);

    // ---------------------------------------------------------------- preconditions

    /// <summary>
    /// Fails the test unless <c>$et-LineAdded</c> really does hold a link that no longer resolves. Without
    /// this, "every link resolves in the rewritten store" is a claim about a source that never had a problem.
    /// </summary>
    public async Task AssertDanglingLinkExistsAsync()
    {
        await using var client = new KurrentDBClient(KurrentDBClientSettings.Create(ConnectionString));
        var (total, dangling) = await CountLinksAsync(client, "$et-LineAdded");
        _output?.WriteLine($"precondition: $et-LineAdded has {total} link(s), {dangling} dangling");
        dangling.Should().BeGreaterThan(0,
            "the whole point of the pure-copy scenario is a source store whose $et-LineAdded holds links to "
            + "events that no longer exist — if the fixture did not produce one, the scenario proves nothing");
    }

    private async Task WaitUntilDanglingAsync(KurrentDBClient client, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var (_, dangling) = await CountLinksAsync(client, "$et-LineAdded");
            if (dangling > 0) return;
            await Task.Delay(500);
        }
        throw new InvalidOperationException(
            "The fixture could not produce a dangling link in $et-LineAdded within "
            + $"{timeout.TotalSeconds:0}s — the scenarios that depend on it would be meaningless, so the "
            + "fixture fails loudly instead of running them.");
    }

    /// <summary>The status string of a projection on the container under test, or <c>null</c> if absent.</summary>
    public async Task<string?> ProjectionStatusAsync(string name)
    {
        await using var projections = NewProjections();
        try { return (await projections.GetStatusAsync(name))?.Status; }
        catch { return null; }
    }

    /// <summary>Polls until <paramref name="name"/> reports Faulted, or returns the last status seen.</summary>
    public async Task<string?> WaitForFaultedAsync(string name, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        string? last = null;
        while (DateTime.UtcNow < deadline)
        {
            last = await ProjectionStatusAsync(name);
            if (last?.Contains("Faulted", StringComparison.OrdinalIgnoreCase) == true) return last;
            await Task.Delay(500);
        }
        return last;
    }

    /// <summary>Polls until <paramref name="name"/> reports Running, or returns the last status seen.</summary>
    public async Task<string?> WaitForRunningAsync(string name, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        string? last = null;
        while (DateTime.UtcNow < deadline)
        {
            last = await ProjectionStatusAsync(name);
            if (last?.Contains("Running", StringComparison.OrdinalIgnoreCase) == true) return last;
            await Task.Delay(500);
        }
        return last;
    }

    /// <summary>Reads a link stream and reports how many links do not resolve to an event.</summary>
    public static async Task<(int Total, int Dangling)> CountLinksAsync(KurrentDBClient client, string stream)
    {
        var res = client.ReadStreamAsync(Direction.Forwards, stream, StreamPosition.Start, resolveLinkTos: true);
        if (await res.ReadState == ReadState.StreamNotFound) return (0, 0);
        int total = 0, dangling = 0;
        await foreach (var re in res)
        {
            total++;
            if (re.Event is null) dangling++;
        }
        return (total, dangling);
    }

    private static async Task WaitForLinkCountAsync(KurrentDBClient client, string stream, int atLeast,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        long last = -1;
        while (DateTime.UtcNow < deadline)
        {
            var res = client.ReadStreamAsync(Direction.Forwards, stream, StreamPosition.Start,
                resolveLinkTos: false);
            last = await res.ReadState == ReadState.StreamNotFound ? 0 : await res.LongCountAsync();
            if (last >= atLeast) return;
            await Task.Delay(250);
        }
        // A silent timeout here would let a scenario run against a half-built fixture and then report a
        // conclusion about KurrentDB that is really a conclusion about a starved test host.
        throw new InvalidOperationException(
            $"Fixture setup: '{stream}' only reached {last} of the {atLeast} expected link(s) in "
            + $"{timeout.TotalSeconds:0}s.");
    }

    // ---------------------------------------------------------------- reading results

    /// <summary>
    /// Opens a client against the ORIGINAL container. Every assertion in every scenario reads through this —
    /// never through the scratch store, which would prove only that the copy engine can write.
    /// </summary>
    public KurrentDBClient NewClient() => new(KurrentDBClientSettings.Create(ConnectionString));

    /// <summary>A projection-management client against the original container.</summary>
    public KurrentDBProjectionManagementClient NewProjections() =>
        new(KurrentDBClientSettings.Create(ConnectionString));

    /// <summary>Reads a stream through the original container; empty when it does not exist.</summary>
    public async Task<List<EventRecord>> ReadAsync(string stream, bool resolveLinkTos = false)
    {
        await using var client = NewClient();
        var res = client.ReadStreamAsync(Direction.Forwards, stream, StreamPosition.Start,
            resolveLinkTos: resolveLinkTos);
        if (await res.ReadState == ReadState.StreamNotFound) return [];
        var list = new List<EventRecord>();
        await foreach (var e in res) list.Add(resolveLinkTos ? e.Event : e.OriginalEvent);
        return list;
    }

    /// <summary>Whether a stream exists in the store right now.</summary>
    public async Task<bool> StreamExistsAsync(string stream) => (await ReadAsync(stream)).Count > 0;

    /// <summary>The subdirectory names directly under <see cref="RootDir"/>.</summary>
    public IReadOnlyList<string> RootEntries() =>
        Directory.EnumerateDirectories(RootDir).Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal)
            .ToList()!;

    /// <summary>The container's start time, so a scenario can prove it was never restarted.</summary>
    public async Task<string> StartedAtAsync() =>
        (await _docker.Containers.InspectContainerAsync(ContainerId)).State.StartedAt;

    /// <summary>Container names currently running that carry the fixture's compose label.</summary>
    public async Task<IReadOnlyList<string>> ScratchContainersAsync()
    {
        var all = await _docker.Containers.ListContainersAsync(new ContainersListParameters { All = true });
        return all.SelectMany(c => c.Names ?? [])
            .Select(n => n.TrimStart('/'))
            .Where(n => n.StartsWith("mp-rewrite-" + ContainerName, StringComparison.Ordinal))
            .ToList();
    }

    /// <summary>
    /// Starts a second container carrying the SAME compose label, so the sibling guard has a real sibling.
    /// </summary>
    /// <remarks>
    /// It runs the store image with a <c>sleep</c> entrypoint rather than pulling a second image — this host's
    /// root filesystem is at 92 %, and the guard cares about the label, not about what the sibling does.
    /// </remarks>
    public async Task<string> StartSiblingAsync()
    {
        var name = $"mp-rewrite-test-sibling-{Guid.NewGuid():N}"[..40];
        var created = await _docker.Containers.CreateContainerAsync(new CreateContainerParameters
        {
            Image = Image,
            Name = name,
            Labels = new Dictionary<string, string> { [DockerStore.ComposeProjectLabel] = ComposeProject },
            Entrypoint = ["/bin/sh", "-c", "sleep 600"]
        });
        _extraContainers.Add(created.ID);
        await _docker.Containers.StartContainerAsync(created.ID, new ContainerStartParameters());
        return name;
    }

    /// <summary>The docker client the tool is handed.</summary>
    public IDockerClient Docker => _docker;

    /// <summary>The logger factory the tool is handed.</summary>
    public ILoggerFactory Loggers => _loggerFactory;

    /// <summary>
    /// Waits until no client has a gRPC call open. The connected-client guard is about the OPERATOR's clients;
    /// a scenario that left its own seeding subscription open would be testing the test.
    /// </summary>
    public async Task WaitUntilNoClientsAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var (grpc, _) = await DockerStore.ReadConnectionMetricsAsync(ConnectionString);
            if (grpc == 0) return;
            await Task.Delay(200);
        }
        throw new InvalidOperationException(
            $"The store still reports open gRPC calls after {timeout.TotalSeconds:0}s; the scenario's own "
            + "client would be mistaken for the operator's.");
    }

    private static int FreePort()
    {
        using var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start();
        var p = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }

    // ---------------------------------------------------------------- teardown

    public async ValueTask DisposeAsync()
    {
        var store = new DockerStore(_docker, NullLogger.Instance);
        foreach (var id in _extraContainers) await Safe(() => store.RemoveAsync(id));
        await Safe(() => store.RemoveAsync(ContainerId));
        foreach (var name in await SafeList()) await Safe(() => store.RemoveAsync(name));

        // Every directory under the root was filled by a container running as uid 1001; the test process
        // cannot delete those files itself (measured: Permission denied on index/stream-existence).
        foreach (var dir in Directory.Exists(RootDir) ? Directory.EnumerateDirectories(RootDir).ToList() : [])
            await Safe(() => store.PurgeDirectoryAsync(Image, dir));
        await Safe(() => { if (Directory.Exists(RootDir)) Directory.Delete(RootDir, recursive: true); return Task.CompletedTask; });

        _docker.Dispose();
        _loggerFactory.Dispose();
    }

    private async Task<IReadOnlyList<string>> SafeList()
    {
        try { return await ScratchContainersAsync(); }
        catch { return []; }
    }

    private static async Task Safe(Func<Task> action)
    {
        try { await action(); }
        catch { /* teardown must never mask the test's own failure */ }
    }
}

/// <summary>Routes the tool's logging into xunit's output so a failed scenario shows what the tool did.</summary>
internal sealed class TestOutputLoggerProvider(ITestOutputHelper output) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new XunitLogger(output, categoryName);
    public void Dispose() { }

    private sealed class XunitLogger(ITestOutputHelper output, string category) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(level)) return;
            try { output.WriteLine($"[{level}] {category}: {formatter(state, ex)}{(ex is null ? "" : " " + ex.Message)}"); }
            catch { /* the test has already finished writing */ }
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
