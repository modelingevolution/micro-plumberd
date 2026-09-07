using System.Globalization;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;

namespace MicroPlumberd.Rewrite;

/// <summary>Raised when the tool refuses to proceed. Carries the exit code the process must return.</summary>
public sealed class RewriteRefusedException(ExitCode code, string message) : Exception(message)
{
    /// <summary>The exit code this refusal maps to.</summary>
    public ExitCode Code { get; } = code;
}

/// <summary>Where a container keeps its event-store data on the host.</summary>
/// <param name="StoreDir">Host path of the data directory itself (what is renamed away on a swap).</param>
/// <param name="Destination">The path the data directory is mounted at INSIDE the container.</param>
/// <param name="IsBind">True for a bind mount (the supported case); false for a named volume.</param>
/// <param name="VolumeName">The named volume, when <paramref name="IsBind"/> is false.</param>
public sealed record DataLocation(string? StoreDir, string Destination, bool IsBind, string? VolumeName)
{
    /// <summary>The directory the data directory lives IN — backups and the new store are created here.</summary>
    public string Parent => Path.GetDirectoryName(StoreDir
        ?? throw new InvalidOperationException("A named volume has no host path."))!;

    /// <summary>The data directory's own name (usually <c>data</c>) — backups are named after it.</summary>
    public string Name => Path.GetFileName(StoreDir!.TrimEnd(Path.DirectorySeparatorChar));
}

/// <summary>The inspected KurrentDB container the tool operates on.</summary>
public sealed record StoreContainer
{
    /// <summary>Full docker id.</summary>
    public required string Id { get; init; }

    /// <summary>Container name without the leading slash.</summary>
    public required string Name { get; init; }

    /// <summary>Image reference — the scratch store is started from the same one.</summary>
    public required string Image { get; init; }

    /// <summary>The container's environment, verbatim.</summary>
    public required IReadOnlyList<string> Env { get; init; }

    /// <summary>The <c>com.docker.compose.project</c> label, or <c>null</c> when not compose-managed.</summary>
    public string? ComposeProject { get; init; }

    /// <summary>Where its data lives.</summary>
    public required DataLocation Data { get; init; }

    /// <summary>Whether the container was running when inspected.</summary>
    public required bool Running { get; init; }

    /// <summary>Host port 2113 is published on (<c>null</c> when nothing is published).</summary>
    public int? PublishedPort { get; init; }

    /// <summary>The container's own bridge address — the fallback when no port is published.</summary>
    public string? BridgeIp { get; init; }
}

/// <summary>
/// Everything the tool does to the docker host and the host filesystem. Kept apart from
/// <see cref="RewriteCommand"/> so the orchestration reads as the seven steps of the design and the docker
/// details are in one place.
/// </summary>
public sealed partial class DockerStore(IDockerClient client, ILogger logger)
{
    /// <summary>The label compose stamps on every container of a project.</summary>
    public const string ComposeProjectLabel = "com.docker.compose.project";

    /// <summary>Env keys that name a non-default data directory, most specific first.</summary>
    private static readonly string[] DbPathEnvKeys = ["KURRENTDB_DB", "EVENTSTORE_DB"];

    /// <summary>Default in-container data directories, newest product name first.</summary>
    private static readonly string[] DefaultDbPaths = ["/var/lib/kurrentdb", "/var/lib/eventstore"];

    /// <summary>
    /// Settings that point at STATE which must move with the data. If one of these resolves outside the
    /// directory being swapped, the rewrite cannot be correct and the tool refuses.
    /// </summary>
    /// <remarks>
    /// The index is the dangerous one: the scratch store builds an index for the NEW log, but if the original
    /// container keeps its index somewhere this tool does not swap, it comes back on new data with a stale
    /// index — silent and catastrophic, and the same shape as the in-memory-database case (D13). Refusing an
    /// unsupported layout is the posture the owner already chose for named volumes (ADR 7, decision 7);
    /// replicating arbitrary extra mounts is not iteration-1 scope.
    /// </remarks>
    private static readonly string[] StatePathEnvKeys =
        ["KURRENTDB_DB", "KURRENTDB_INDEX", "EVENTSTORE_DB", "EVENTSTORE_INDEX"];

    /// <summary>
    /// Settings that point at a path which is NOT state. Reported, never refused — see
    /// <see cref="CheckStatePathsInsideMount"/>.
    /// </summary>
    private static readonly string[] DiagnosticPathEnvKeys = ["KURRENTDB_LOG", "EVENTSTORE_LOG"];

    /// <summary>The metric that counts gRPC calls a client currently has open against the node.</summary>
    /// <remarks>
    /// Measured on KurrentDB 26.1 (2026-09-07): <c>/stats</c> has <c>proc/tcp/connections</c>, which counts
    /// the LEGACY TCP client protocol only and stays 0 for every gRPC client — it cannot serve as this guard.
    /// The Prometheus endpoint <c>/metrics</c> exposes <c>kurrentdb_current_incoming_grpc_calls</c>: 0 with no
    /// client, 2 with one open <c>$all</c> subscription, back to 0 once the client process exits.
    /// <c>kurrentdb_kestrel_connections</c> counts the tool's OWN scrape, so it is reported but never gated on.
    /// </remarks>
    public const string OpenGrpcCallsMetric = "kurrentdb_current_incoming_grpc_calls";

    /// <summary>Reported alongside the guard, never gated on — the tool's own HTTP request is one of them.</summary>
    public const string KestrelConnectionsMetric = "kurrentdb_kestrel_connections";

    /// <summary>The fleet default, used when the operator names no credentials.</summary>
    /// <remarks>
    /// Kept as a DEFAULT rather than demanded up front: on this fleet it is correct, and a hard fail-fast
    /// would tax every 3 a.m. invocation. What makes that safe is that a wrong password now says so
    /// (<see cref="ReadConnectionMetricsAsync"/> separates 401/403 from unreachable) instead of sending the
    /// operator to diagnose a store that is answering perfectly well.
    /// </remarks>
    public const string DefaultUser = "admin";

    /// <inheritdoc cref="DefaultUser"/>
    public const string DefaultPassword = "changeit";

    // ---------------------------------------------------------------- inspect

    /// <summary>Inspects the target container. Refuses (exit 3) when docker or the container is not there.</summary>
    public async Task<StoreContainer> InspectAsync(string container, CancellationToken ct = default)
    {
        ContainerInspectResponse r;
        try
        {
            r = await client.Containers.InspectContainerAsync(container, ct).ConfigureAwait(false);
        }
        catch (DockerContainerNotFoundException)
        {
            throw new RewriteRefusedException(ExitCode.DockerUnavailable,
                $"No such container: '{container}'.");
        }
        catch (Exception ex) when (ex is HttpRequestException or DockerApiException or IOException)
        {
            throw new RewriteRefusedException(ExitCode.DockerUnavailable,
                $"Cannot reach the docker daemon: {ex.Message}");
        }

        var env = (r.Config?.Env ?? []).ToList();
        var data = ResolveDataLocation(env, r.Mounts ?? []);

        var labels = r.Config?.Labels;
        string? project = null;
        labels?.TryGetValue(ComposeProjectLabel, out project);

        return new StoreContainer
        {
            Id = r.ID,
            Name = r.Name.TrimStart('/'),
            Image = r.Config?.Image ?? r.Image,
            Env = env,
            ComposeProject = string.IsNullOrWhiteSpace(project) ? null : project,
            Data = data,
            Running = r.State?.Running ?? false,
            PublishedPort = ReadPublishedPort(r),
            BridgeIp = ReadBridgeIp(r)
        };
    }

    /// <summary>
    /// Picks the mount that holds the store: the one whose destination is the env-configured DB path when one
    /// is set, otherwise the first default path present. UT-08.
    /// </summary>
    /// <remarks>
    /// The env setting WINS over the defaults, and deliberately so: a container configured with
    /// <c>KURRENTDB_DB=/data</c> that also happens to mount something at <c>/var/lib/kurrentdb</c> would
    /// otherwise have the wrong directory renamed away — the one failure in this tool that cannot be undone
    /// by looking at a backup, because the backup would be of the wrong thing.
    /// </remarks>
    public static DataLocation ResolveDataLocation(IReadOnlyList<string> env, IList<MountPoint> mounts)
    {
        var configured = DbPathEnvKeys.Select(k => EnvValue(env, k)).FirstOrDefault(v => v is not null);
        var candidates = configured is not null ? new[] { configured } : DefaultDbPaths;

        foreach (var dest in candidates)
        {
            var m = mounts.FirstOrDefault(x => PathEquals(x.Destination, dest));
            if (m is null) continue;
            var isBind = string.Equals(m.Type, "bind", StringComparison.OrdinalIgnoreCase);
            return new DataLocation(isBind ? m.Source : null, dest, isBind, isBind ? null : m.Name);
        }

        throw new RewriteRefusedException(ExitCode.GuardRefusal,
            $"The container has no mount at its data directory ({string.Join(" or ", candidates)}), so its "
            + "store lives inside the container's writable layer and cannot be swapped. Mount the data "
            + "directory (a bind mount is what this tool supports) before rewriting it.");
    }

    /// <summary>
    /// Refuses when a path-valued STATE setting resolves outside the directory this tool swaps, and reports
    /// (without refusing) a log path that does.
    /// </summary>
    /// <remarks>
    /// <b>Deviation, deliberate and flagged:</b> the review list names <c>KURRENTDB_LOG</c> alongside
    /// <c>_DB</c>/<c>_INDEX</c>. Logs are not state — nothing about a log path outside the mount makes a
    /// rewrite incorrect, and KurrentDB's own default (<c>/var/log/kurrentdb</c>) IS outside the data
    /// directory. Refusing on it would block a legitimate 3 a.m. repair, with no override, for no safety gain
    /// — the same "false refusal blocks the repair" hazard this tool is otherwise careful about. So a log path
    /// is surfaced in the report and never gates the run.
    /// </remarks>
    public static (IReadOnlyList<string> Refusals, IReadOnlyList<string> Notes) CheckStatePathsInsideMount(
        IReadOnlyList<string> env, DataLocation data)
    {
        var refusals = new List<string>();
        var notes = new List<string>();

        foreach (var key in StatePathEnvKeys)
        {
            var value = EnvValue(env, key);
            if (value is null || IsInside(value, data.Destination)) continue;
            refusals.Add($"{key}={value} resolves outside the directory this tool swaps "
                         + $"({data.Destination}), so the container would come back on the new log with state "
                         + $"this rewrite never touched. Move it inside {data.Destination} and try again.");
        }

        foreach (var key in DiagnosticPathEnvKeys)
        {
            var value = EnvValue(env, key);
            if (value is null || IsInside(value, data.Destination)) continue;
            notes.Add($"note         : {key}={value} is outside {data.Destination}; it is not state, so it is "
                      + "not swapped and not a reason to refuse");
        }

        return (refusals, notes);
    }

    /// <summary>Whether a container path is the mount root or sits beneath it.</summary>
    internal static bool IsInside(string path, string root)
    {
        var p = path.TrimEnd('/');
        var r = root.TrimEnd('/');
        return string.Equals(p, r, StringComparison.Ordinal)
               || p.StartsWith(r + "/", StringComparison.Ordinal);
    }

    /// <summary>Reads a <c>KEY=value</c> entry out of a container's environment.</summary>
    public static string? EnvValue(IReadOnlyList<string> env, string key)
    {
        foreach (var e in env)
        {
            var i = e.IndexOf('=');
            if (i > 0 && e.AsSpan(0, i).SequenceEqual(key)) return e[(i + 1)..];
        }
        return null;
    }

    private static bool PathEquals(string? a, string b) =>
        a is not null && a.TrimEnd('/') == b.TrimEnd('/');

    private static int? ReadPublishedPort(ContainerInspectResponse r)
    {
        var bindings = r.NetworkSettings?.Ports;
        if (bindings is null) return null;
        foreach (var (port, list) in bindings)
        {
            if (!port.StartsWith("2113/", StringComparison.Ordinal) || list is null) continue;
            foreach (var b in list)
                if (int.TryParse(b.HostPort, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) && p > 0)
                    return p;
        }
        return null;
    }

    private static string? ReadBridgeIp(ContainerInspectResponse r)
    {
        var networks = r.NetworkSettings?.Networks;
        if (networks is not null)
            foreach (var n in networks.Values)
                if (!string.IsNullOrWhiteSpace(n.IPAddress)) return n.IPAddress;
        return string.IsNullOrWhiteSpace(r.NetworkSettings?.IPAddress) ? null : r.NetworkSettings.IPAddress;
    }

    /// <summary>
    /// The address the tool reads the OLD store through: its published 2113 port when there is one, else the
    /// container's own bridge address.
    /// </summary>
    /// <remarks>
    /// The fallback matters on the fleet, where compose files publish nothing and the store is reachable only
    /// on the bridge network. It works because the tool runs on the docker host itself; from anywhere else the
    /// bridge address is unroutable and the operator must publish the port.
    /// </remarks>
    public static string ConnectionString(StoreContainer c, string user = DefaultUser, string password = DefaultPassword)
    {
        var credentials = $"{Uri.EscapeDataString(user)}:{Uri.EscapeDataString(password)}";
        if (c.PublishedPort is { } port)
            return $"esdb://{credentials}@127.0.0.1:{port}?tls=false&tlsVerifyCert=false";
        if (c.BridgeIp is { } ip)
            return $"esdb://{credentials}@{ip}:2113?tls=false&tlsVerifyCert=false";
        throw new RewriteRefusedException(ExitCode.GuardRefusal,
            $"Container '{c.Name}' publishes no port for 2113 and has no bridge address — the tool cannot "
            + "read its store. Publish 2113 on the host and try again.");
    }

    // ---------------------------------------------------------------- guards

    /// <summary>Running containers of the same compose project, excluding the target itself.</summary>
    public async Task<IReadOnlyList<string>> FindRunningSiblingsAsync(StoreContainer c, CancellationToken ct = default)
    {
        if (c.ComposeProject is null) return [];

        var all = await client.Containers.ListContainersAsync(new ContainersListParameters { All = false }, ct)
            .ConfigureAwait(false);
        return all
            .Where(x => x.ID != c.Id)
            .Where(x => x.Labels is not null
                        && x.Labels.TryGetValue(ComposeProjectLabel, out var p)
                        && string.Equals(p, c.ComposeProject, StringComparison.Ordinal))
            .Select(x => x.Names?.FirstOrDefault()?.TrimStart('/') ?? x.ID[..12])
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// gRPC calls a client currently has open against the store, and the raw kestrel connection count.
    /// Reads <c>/metrics</c> over plain HTTP — which is NOT a gRPC call, so the tool never counts itself.
    /// </summary>
    public static async Task<(int OpenGrpcCalls, int KestrelConnections)> ReadConnectionMetricsAsync(
        string connectionString, CancellationToken ct = default)
    {
        var (baseUri, user, pass) = KurrentHttpEndpoint.Parse(connectionString);
        using var http = KurrentHttpEndpoint.CreateClient(user, pass);
        string text;
        try
        {
            using var resp = await http.GetAsync(new Uri(baseUri, "metrics"), ct).ConfigureAwait(false);
            // A rejected LOGIN is a different problem from a store that is down, and telling them apart is the
            // difference between "check your password" and an hour spent diagnosing a healthy store.
            if (resp.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                throw new RewriteRefusedException(ExitCode.GuardRefusal,
                    $"The store at {baseUri} rejected the credentials ({(int)resp.StatusCode} "
                    + $"{resp.StatusCode}). It is running and reachable — the user or password is wrong. Pass "
                    + "--user/--password, or set MP_REWRITE_USER / MP_REWRITE_PASSWORD.");
            resp.EnsureSuccessStatusCode();
            text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new RewriteRefusedException(ExitCode.DockerUnavailable,
                $"The store at {baseUri} did not answer /metrics ({ex.Message}). The connected-client guard "
                + "cannot be evaluated, and running blind would risk losing a live writer's data.");
        }
        return (ReadMetric(text, OpenGrpcCallsMetric), ReadMetric(text, KestrelConnectionsMetric));
    }

    /// <summary>Reads a single-sample Prometheus gauge out of a <c>/metrics</c> body.</summary>
    /// <remarks>
    /// Returns -1 when the metric is ABSENT, which the caller must treat as "cannot tell" rather than "zero".
    /// A future KurrentDB that renames the counter would otherwise silently turn this guard off — the exact
    /// shape of failure the guard exists to prevent.
    /// </remarks>
    internal static int ReadMetric(string metricsBody, string name)
    {
        foreach (var line in metricsBody.Split('\n'))
        {
            var l = line.AsSpan().Trim();
            if (!l.StartsWith(name)) continue;
            var rest = l[name.Length..];
            if (rest.Length > 0 && rest[0] == '{')
            {
                var close = rest.IndexOf('}');
                if (close < 0) continue;
                rest = rest[(close + 1)..];
            }
            else if (rest.Length > 0 && rest[0] != ' ') continue; // a longer metric name that merely starts the same

            var parts = rest.Trim().ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture,
                    out var v))
                return (int)v;
        }
        return -1;
    }
}
