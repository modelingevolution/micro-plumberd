using Docker.DotNet;
using Docker.DotNet.Models;

namespace MicroPlumberd.Testing;

/// <summary>
/// Docker plumbing shared by the container-backed test servers (<see cref="PostgresServer"/>,
/// <see cref="SqlServerServer"/>). Internal on purpose: the shipped surface is the servers themselves.
/// </summary>
static class DockerServerSupport
{
    /// <summary>
    /// Pull <paramref name="image"/> if it is not already present locally.
    /// </summary>
    /// <remarks>
    /// Without this, container creation fails outright on any machine that has not happened to cache the
    /// image — i.e. it works on a dev box and breaks on a clean CI runner.
    /// </remarks>
    public static async Task EnsureImage(DockerClient client, string image, CancellationToken cancellationToken)
    {
        if (await ImageExists(client, image, cancellationToken)) return;

        var (repository, tag) = SplitTag(image);
        await client.Images.CreateImageAsync(
            new ImagesCreateParameters { FromImage = repository, Tag = tag },
            null,
            new Progress<JSONMessage>(),
            cancellationToken);

        // A pull that quietly resolves nothing would otherwise surface later as an opaque create failure.
        if (!await ImageExists(client, image, cancellationToken))
            throw new InvalidOperationException(
                $"Docker image '{image}' is still not present after attempting to pull it. " +
                "Check connectivity to the registry and that the tag exists.");
    }

    static async Task<bool> ImageExists(DockerClient client, string image, CancellationToken cancellationToken)
    {
        try
        {
            await client.Images.InspectImageAsync(image, cancellationToken);
            return true;
        }
        catch (DockerImageNotFoundException)
        {
            return false;
        }
    }

    /// <summary>Splits "repo/name:tag" into its repository and tag, defaulting the tag to "latest".</summary>
    /// <remarks>The last ':' is only a tag separator when it comes after the last '/' — otherwise it is the
    /// port of a registry host (e.g. "localhost:5000/img").</remarks>
    static (string Repository, string Tag) SplitTag(string image)
    {
        var colon = image.LastIndexOf(':');
        var slash = image.LastIndexOf('/');
        return colon > slash && colon >= 0
            ? (image[..colon], image[(colon + 1)..])
            : (image, "latest");
    }

    /// <summary>
    /// True when a Docker daemon is reachable. Cheap: one ping, a short timeout, never starts anything.
    /// </summary>
    /// <remarks>
    /// Exists so callers can decide "skip this test?" before any fixture runs. A test that needs Docker must
    /// be able to SKIP loudly when Docker is absent — never pass by accident — and it cannot pay a
    /// start-up timeout to find that out.
    /// </remarks>
    public static async Task<bool> IsDockerAvailable(TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);
            using var client = new DockerClientConfiguration().CreateClient();
            await client.System.PingAsync(cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Removes any container already holding this name, so the caller always gets a FRESH one.
    /// </summary>
    /// <remarks>
    /// Adopting an existing container by name (as EventStoreServer does) silently inherits a stale
    /// container's environment and data from a previous run — the fixture then tests something other than
    /// what it configured, and the symptom appears somewhere else entirely.
    /// </remarks>
    public static async Task RemoveStaleContainer(DockerClient client, string containerName,
        CancellationToken cancellationToken)
    {
        var existing = await FindContainer(client, containerName, cancellationToken);
        if (existing == null) return;
        await Cleanup(client, containerName);
    }

    public static async Task<ContainerListResponse?> FindContainer(DockerClient client, string containerName,
        CancellationToken cancellationToken = default)
    {
        var containers = await client.Containers.ListContainersAsync(
            new ContainersListParameters { All = true, Limit = 10000 }, cancellationToken);

        // Docker reports names with a leading '/'; match the whole name so "pg-5501" cannot match "pg-55010".
        return containers.FirstOrDefault(c => c.Names.Any(n => n.TrimStart('/') == containerName));
    }

    /// <summary>Runs a command inside the container and returns its exit code and output.</summary>
    /// <remarks>
    /// This is how readiness is probed and databases are created without taking a dependency on Npgsql or
    /// SqlClient — a testing package has no business dragging database drivers in behind its users.
    /// </remarks>
    public static async Task<(long ExitCode, string Stdout, string Stderr)> Exec(DockerClient client,
        string containerId, IList<string> command, CancellationToken cancellationToken)
    {
        var exec = await client.Exec.ExecCreateContainerAsync(containerId,
            new ContainerExecCreateParameters { AttachStdout = true, AttachStderr = true, Cmd = command },
            cancellationToken);

        using var stream = await client.Exec.StartAndAttachContainerExecAsync(exec.ID, false, cancellationToken);
        var (stdout, stderr) = await stream.ReadOutputToEndAsync(cancellationToken);

        var inspect = await client.Exec.InspectContainerExecAsync(exec.ID, cancellationToken);
        return (inspect.ExitCode, stdout, stderr);
    }

    /// <summary>
    /// Polls <paramref name="probe"/> until it reports success, or throws on timeout with the last failure.
    /// </summary>
    /// <remarks>
    /// Never sleep a fixed duration instead of this: a database accepts TCP well before it accepts queries,
    /// and a fixed sleep is either slower than necessary or flaky — usually both, on different machines.
    /// <para>
    /// <paramref name="assertStillAlive"/> runs OUTSIDE the probe's catch, so a container that has died
    /// aborts the wait immediately. Without it a dead container burns the whole timeout and then reports a
    /// READINESS failure — blaming the probe for something that was never going to become ready, which is
    /// the difference between a two-minute fix and an hour chasing a phantom.
    /// </para>
    /// </remarks>
    public static async Task WaitUntilReady(Func<CancellationToken, Task<(bool Ready, string Diagnostic)>> probe,
        Func<CancellationToken, Task>? assertStillAlive, TimeSpan timeout, string what,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        var delay = TimeSpan.FromMilliseconds(250);
        var last = "no probe completed";

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (assertStillAlive != null) await assertStillAlive(cancellationToken);

            try
            {
                var (ready, diagnostic) = await probe(cancellationToken);
                if (ready) return;
                last = diagnostic;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                last = ex.Message;
            }

            await Task.Delay(delay, cancellationToken);
            if (delay < TimeSpan.FromSeconds(2)) delay += TimeSpan.FromMilliseconds(250);
        }

        throw new TimeoutException(
            $"{what} did not become ready within {timeout.TotalSeconds:F0}s. Last probe result: {last}");
    }

    /// <summary>
    /// Throws if the container has exited, quoting its logs.
    /// </summary>
    /// <remarks>Containers can die on startup for reasons that have nothing to do with readiness — SQL
    /// Server exits silently when MSSQL_SA_PASSWORD fails its complexity policy. The logs say exactly that;
    /// a readiness timeout says nothing useful.</remarks>
    public static async Task AssertContainerAlive(DockerClient client, string containerName,
        CancellationToken cancellationToken)
    {
        var container = await FindContainer(client, containerName, cancellationToken);
        if (container == null)
            throw new InvalidOperationException($"Container '{containerName}' disappeared while starting.");

        var state = await client.Containers.InspectContainerAsync(container.ID, cancellationToken);
        if (state.State.Running) return;

        var logs = await TailLogs(client, container.ID, cancellationToken);
        throw new InvalidOperationException(
            $"Container '{containerName}' exited with code {state.State.ExitCode} instead of starting. " +
            $"It will never become ready.\n--- docker logs ---\n{logs}");
    }

    static async Task<string> TailLogs(DockerClient client, string containerId, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = await client.Containers.GetContainerLogsAsync(containerId, false,
                new ContainerLogsParameters { ShowStdout = true, ShowStderr = true, Tail = "40" },
                cancellationToken);
            var (stdout, stderr) = await stream.ReadOutputToEndAsync(cancellationToken);
            return string.IsNullOrWhiteSpace(stderr) ? stdout.Trim() : $"{stdout}\n{stderr}".Trim();
        }
        catch (Exception ex)
        {
            return $"(could not read logs: {ex.Message})";
        }
    }

    /// <summary>True when Docker refused to publish the port because something already holds it.</summary>
    /// <remarks>
    /// This machine is WSL2. Under MIRRORED networking, ports are SHARED between WSL and Windows, so a port
    /// that binds fine inside WSL can already be held on the Windows side — PortSearcher only checks the WSL
    /// side. It is TOCTOU besides: it binds, closes, then returns, so the port can be taken before Docker
    /// gets it. Both make a bind failure NORMAL and retryable, not fatal.
    /// </remarks>
    public static bool IsPortUnavailable(Exception ex)
    {
        var text = ex switch
        {
            DockerApiException api => api.ResponseBody ?? api.Message,
            _ => ex.Message
        };
        return text.Contains("port is already allocated", StringComparison.OrdinalIgnoreCase)
            || text.Contains("address already in use", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Ports are not available", StringComparison.OrdinalIgnoreCase)
            || text.Contains("bind: An attempt was made to access a socket", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Stops and removes the container, if it exists. Safe to call when it does not.</summary>
    public static async Task Cleanup(DockerClient client, string containerName)
    {
        var container = await FindContainer(client, containerName);
        if (container == null) return;

        try
        {
            await client.Containers.RemoveContainerAsync(container.ID,
                new ContainerRemoveParameters { Force = true, RemoveVolumes = true });
        }
        catch (DockerContainerNotFoundException)
        {
            // Already gone — that is the outcome we wanted.
        }
    }

    /// <summary>
    /// Guards an identifier that is about to be interpolated into SQL that no driver will parameterise.
    /// </summary>
    /// <remarks>Database names cannot be passed as parameters to CREATE/DROP DATABASE in any engine, so the
    /// only safe move is to refuse anything that is not a plain identifier.</remarks>
    public static string ValidateDatabaseName(string database)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(database);
        if (database.Length > 63 || !database.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
            throw new ArgumentException(
                $"Database name '{database}' must be 1-63 characters of ASCII letters, digits or underscore.",
                nameof(database));
        return database;
    }
}
