using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;

namespace MicroPlumberd.Rewrite;

public sealed partial class DockerStore
{
    /// <summary>Pulls <paramref name="image"/> when it is not present locally. Refuses with exit 3 if it cannot.</summary>
    public async Task EnsureImageAsync(string image, CancellationToken ct = default)
    {
        if (await ImageExistsAsync(image, ct).ConfigureAwait(false)) return;

        var (repository, tag) = SplitTag(image);
        logger.LogInformation("Pulling image {Image}…", image);
        try
        {
            await client.Images.CreateImageAsync(new ImagesCreateParameters { FromImage = repository, Tag = tag },
                null, new Progress<JSONMessage>(), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is DockerApiException or HttpRequestException or IOException)
        {
            throw new RewriteRefusedException(ExitCode.DockerUnavailable,
                $"Could not pull image '{image}': {ex.Message}");
        }

        // A pull that quietly resolves nothing would otherwise surface as an opaque container-create failure.
        if (!await ImageExistsAsync(image, ct).ConfigureAwait(false))
            throw new RewriteRefusedException(ExitCode.DockerUnavailable,
                $"Image '{image}' is still not present after pulling it.");
    }

    private async Task<bool> ImageExistsAsync(string image, CancellationToken ct)
    {
        try
        {
            await client.Images.InspectImageAsync(image, ct).ConfigureAwait(false);
            return true;
        }
        catch (DockerImageNotFoundException) { return false; }
    }

    private static (string Repository, string Tag) SplitTag(string image)
    {
        var colon = image.LastIndexOf(':');
        var slash = image.LastIndexOf('/');
        return colon > slash && colon >= 0 ? (image[..colon], image[(colon + 1)..]) : (image, "latest");
    }

    /// <summary>Stops a container and waits for it to actually be stopped.</summary>
    public async Task StopAsync(string id, CancellationToken ct = default)
    {
        await client.Containers.StopContainerAsync(id, new ContainerStopParameters { WaitBeforeKillSeconds = 30 }, ct)
            .ConfigureAwait(false);
        logger.LogInformation("Stopped container {Id}.", Short(id));
    }

    /// <summary>Starts a container.</summary>
    public async Task StartAsync(string id, CancellationToken ct = default)
    {
        await client.Containers.StartContainerAsync(id, new ContainerStartParameters(), ct).ConfigureAwait(false);
        logger.LogInformation("Started container {Id}.", Short(id));
    }

    /// <summary>Force-removes a container, ignoring the case where it is already gone.</summary>
    public async Task RemoveAsync(string id, CancellationToken ct = default)
    {
        try
        {
            await client.Containers.RemoveContainerAsync(id, new ContainerRemoveParameters { Force = true }, ct)
                .ConfigureAwait(false);
            logger.LogInformation("Removed container {Id}.", Short(id));
        }
        catch (DockerContainerNotFoundException) { /* already gone — the desired state */ }
    }

    /// <summary>Whether the container is currently running.</summary>
    public async Task<bool> IsRunningAsync(string id, CancellationToken ct = default)
    {
        try
        {
            var r = await client.Containers.InspectContainerAsync(id, ct).ConfigureAwait(false);
            return r.State?.Running ?? false;
        }
        catch (DockerContainerNotFoundException) { return false; }
    }

    /// <summary>
    /// Creates and starts the scratch store: the SAME image as the old container, its environment plus the
    /// three settings the copy engine requires, bound to <paramref name="hostDataDir"/>, on a random host port
    /// published to loopback only.
    /// </summary>
    /// <remarks>
    /// The scratch store is published on <c>127.0.0.1</c> rather than <c>0.0.0.0</c> because for the minutes it
    /// exists it holds a full copy of production history with authentication effectively disabled
    /// (<c>KURRENTDB_INSECURE=true</c>, which the copy engine needs). It has no business being reachable from
    /// the network.
    /// </remarks>
    public async Task<ScratchStore> StartScratchAsync(StoreContainer old, string hostDataDir, string name,
        CancellationToken ct = default)
    {
        var env = BuildScratchEnv(old.Env);
        var port = FreeTcpPort();

        var created = await client.Containers.CreateContainerAsync(new CreateContainerParameters
        {
            Image = old.Image,
            Name = name,
            Env = env,
            ExposedPorts = new Dictionary<string, EmptyStruct> { ["2113"] = default },
            HostConfig = new HostConfig
            {
                // Bind, never a new network: creating one on this host hands out a subnet that collides with
                // the company VPN. The default bridge plus a loopback-published port is all the tool needs.
                Binds = [$"{hostDataDir}:{old.Data.Destination}"],
                PortBindings = new Dictionary<string, IList<PortBinding>>
                {
                    ["2113"] = [new PortBinding { HostIP = "127.0.0.1", HostPort = port.ToString() }]
                },
                AutoRemove = false
            }
        }, ct).ConfigureAwait(false);

        await client.Containers.StartContainerAsync(created.ID, new ContainerStartParameters(), ct)
            .ConfigureAwait(false);
        logger.LogInformation("Scratch store {Name} started on 127.0.0.1:{Port} over {Dir}.", name, port, hostDataDir);

        return new ScratchStore(created.ID, name, port,
            $"esdb://admin:changeit@127.0.0.1:{port}?tls=false&tlsVerifyCert=false");
    }

    /// <summary>
    /// The old container's environment with the copy engine's three requirements forced on, replacing any
    /// existing value rather than appending a second one (docker keeps the LAST, but a duplicated key in an
    /// inspect output is exactly the kind of thing that makes an incident harder to read).
    /// </summary>
    internal static List<string> BuildScratchEnv(IReadOnlyList<string> oldEnv)
    {
        var forced = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["KURRENTDB_RUN_PROJECTIONS"] = "All",
            ["KURRENTDB_START_STANDARD_PROJECTIONS"] = "true",
            ["KURRENTDB_INSECURE"] = "true",
            // The scratch store must be a REAL store on the bind-mounted directory. An old container running
            // with an in-memory database would otherwise hand the scratch store the same setting and the copy
            // would be written to nothing.
            ["KURRENTDB_MEM_DB"] = "false"
        };

        var result = new List<string>();
        foreach (var e in oldEnv)
        {
            var i = e.IndexOf('=');
            var key = i > 0 ? e[..i] : e;
            if (!forced.ContainsKey(key)) result.Add(e);
        }
        result.AddRange(forced.Select(kv => $"{kv.Key}={kv.Value}"));
        return result;
    }

    private static int FreeTcpPort()
    {
        using var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start();
        var port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    /// <summary>
    /// Scratch containers this tool has running or left behind for <paramref name="containerName"/>, so
    /// <c>--status</c> can tell an operator that a rewrite is in flight — or was interrupted.
    /// </summary>
    public async Task<IReadOnlyList<string>> FindScratchContainersAsync(string containerName,
        CancellationToken ct = default)
    {
        var all = await client.Containers.ListContainersAsync(new ContainersListParameters { All = true }, ct)
            .ConfigureAwait(false);
        var prefix = $"mp-rewrite-{containerName}-";
        return all.SelectMany(x => x.Names ?? [])
            .Select(n => n.TrimStart('/'))
            .Where(n => n.StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
    }

    private static string Short(string id) => id.Length > 12 ? id[..12] : id;
}

/// <summary>A started scratch store: the container the rewrite is copied INTO before the swap.</summary>
/// <param name="Id">Docker container id.</param>
/// <param name="Name">Container name (<c>mp-rewrite-&lt;ctr&gt;-&lt;ts&gt;</c>).</param>
/// <param name="Port">Host port 2113 is published on, bound to loopback.</param>
/// <param name="ConnectionString">KurrentDB connection string for the scratch store.</param>
public sealed record ScratchStore(string Id, string Name, int Port, string ConnectionString);

public sealed partial class DockerStore
{
    /// <summary>
    /// Deletes a directory the STORE CONTAINER wrote into, from inside a throwaway root container.
    /// </summary>
    /// <remarks>
    /// <para>Measured on saturn (2026-09-07), KurrentDB 26.1: the image runs as uid 1001 and creates
    /// <c>index/stream-existence/</c> mode 0755 owned by 1001. An operator running the tool as uid 1000 gets
    /// <c>Permission denied</c> deleting the files inside it, so an ordinary recursive delete of
    /// <c>data.new.&lt;ts&gt;</c> — which a dry run and every pre-swap failure must do — cannot work. Widening
    /// the mode of the directory the tool creates does not help either: the container's own subdirectories are
    /// created with the container's umask, not the parent's.</para>
    /// <para>So the delete is done by the only user who can: root inside a container from the same image, with
    /// the PARENT directory bind-mounted. The tool never needs root on the host.</para>
    /// </remarks>
    public async Task PurgeDirectoryAsync(string image, string dir, CancellationToken ct = default)
    {
        if (!Directory.Exists(dir)) return;

        var full = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar);
        if (Path.GetDirectoryName(full) is null)
            throw new ArgumentException($"Refusing to purge a filesystem root: '{dir}'.", nameof(dir));

        // Try the cheap path first — an empty or tool-owned directory needs no container at all.
        try
        {
            Directory.Delete(full, recursive: true);
            logger.LogInformation("Removed {Dir}.", full);
            return;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            logger.LogInformation("{Dir} holds files written by the container's user; removing its contents "
                                  + "from a root helper container.", full);
        }

        // THE INTERLOCK IS THE MOUNT. The container gets ONLY the directory being deleted, and deletes its
        // CONTENTS — so the blast radius equals that directory by construction, for every caller, and no
        // convention about names or path depth has to hold for that to be true. The parent (which holds the
        // LIVE store and every backup) is never mounted into a root container at all; the now-empty directory
        // is removed from the host afterwards, which needs no privileges.
        var created = await client.Containers.CreateContainerAsync(new CreateContainerParameters
        {
            Image = image,
            Name = $"mp-rewrite-purge-{Guid.NewGuid():N}"[..40],
            User = "0:0",
            Entrypoint = ["/bin/sh", "-c", "rm -rf /purge/..?* /purge/.[!.]* /purge/*"],
            HostConfig = new HostConfig { Binds = [$"{full}:/purge"], AutoRemove = false }
        }, ct).ConfigureAwait(false);

        try
        {
            await client.Containers.StartContainerAsync(created.ID, new ContainerStartParameters(), ct)
                .ConfigureAwait(false);
            var wait = await client.Containers.WaitContainerAsync(created.ID, ct).ConfigureAwait(false);
            if (wait.StatusCode != 0)
                throw new RewriteRefusedException(ExitCode.FilesystemFailure,
                    $"Could not remove '{full}': the helper container exited with {wait.StatusCode}.");
        }
        finally
        {
            await RemoveAsync(created.ID, ct).ConfigureAwait(false);
        }

        // The helper emptied it; removing an empty directory needs no privileges.
        try
        {
            Directory.Delete(full, recursive: true);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            throw new RewriteRefusedException(ExitCode.FilesystemFailure,
                $"'{full}' could not be removed even after its contents were purged: {ex.Message}");
        }
        logger.LogInformation("Removed {Dir} (contents via a root helper container).", full);
    }
}
