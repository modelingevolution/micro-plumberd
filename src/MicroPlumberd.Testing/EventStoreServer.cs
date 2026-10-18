using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using Docker.DotNet;
using Docker.DotNet.Models;
using KurrentDB.Client;

namespace MicroPlumberd.Testing;

/// <summary>
/// A KurrentDB in a Docker container for a test.
/// <para>
/// <b>Every container it creates is removed</b> — by <see cref="DisposeAsync"/>, by a failed
/// <see cref="StartInDocker(bool, bool)"/> (the container exists before readiness is known), at process exit
/// for a server nobody disposed, and, for a process that was killed (where nothing in it can run), by the
/// next <see cref="StartInDocker(bool, bool)"/> on the same machine. Each container is labelled with its
/// owner (machine, pid, process start time) so only a DEAD owner's containers are ever reaped: the Docker
/// daemon is shared, and a name filter over it would hit other sessions' containers.
/// </para>
/// </summary>
public class EventStoreServer :  IDisposable, IAsyncDisposable
{
    /// <summary>Marks a container as created by this class. Nothing without it is ever touched by the reaper.</summary>
    public const string Label = "microplumberd.testing";

    /// <summary>"{machine}/{pid}/{process start ticks}" of the process that created the container.</summary>
    public const string OwnerLabel = "microplumberd.testing.owner";

    private static readonly TimeSpan DefaultReadyTimeout = TimeSpan.FromSeconds(30);
    private static int _starting;

    /// <summary>
    /// The readiness budget when <paramref name="startingAtOnce"/> stores are starting in this process: one store's
    /// budget each. A suite starting stores in parallel shares the host's CPU between their startups, and a fixed
    /// 30 s was one store's budget split between all of them (AggregateTests.Update timed out that way in-suite).
    /// </summary>
    public static TimeSpan ReadyBudget(TimeSpan perStore, int startingAtOnce) => perStore * Math.Max(1, startingAtOnce);

    /// <summary>The readiness wait itself — a seam so a test can see the budget each start actually waited with.</summary>
    /// <summary>
    /// The removal Cleanup makes — a seam so a test can make it fail (as a concurrent removal's 409 did). It is given the
    /// server, so a test touches only its own store while the suite runs classes in parallel.
    /// </summary>
    internal static Func<EventStoreServer, DockerClient, string, Task> RemoveContainer { get; set; } =
        (_, docker, id) => docker.Containers.RemoveContainerAsync(id, new ContainerRemoveParameters { Force = true, RemoveVolumes = true });

    internal static Func<EventStoreServer, KurrentDBClientSettings, TimeSpan, Task> WaitReady { get; set; } =
        (_, settings, budget) => settings.WaitUntilReady(budget);
    private static readonly ConcurrentDictionary<EventStoreServer, byte> Undisposed = new();
    private static readonly string CurrentOwner = OwnerOf(Environment.ProcessId, Process.GetCurrentProcess().StartTime);

    static EventStoreServer() => AppDomain.CurrentDomain.ProcessExit += (_, _) => RemoveUndisposed();

    public KurrentDBClientSettings GetEventStoreSettings() => KurrentDBClientSettings.Create(HttpUrl.ToString());

    public Uri HttpUrl { get; }
    private readonly int httpPort;
    private static PortSearcher _searcher = new PortSearcher();
    private readonly DockerClient client;
    public static EventStoreServer Create(string? containerName = null) => new EventStoreServer(containerName);
    public EventStoreServer() : this(null) {}
    internal EventStoreServer(string? containerName = null)
    {
        if (containerName != null)
            _containerName = containerName;
        (httpPort, _portLock) = PortLock.Acquire(_searcher);
        const string eventStoreHostName = "127.0.0.1";
        HttpUrl = new Uri($"esdb://admin:changeit@{eventStoreHostName}:{httpPort}?tls=false&tlsVerifyCert=false");
        client = new DockerClientConfiguration()
            .CreateClient();
    }

    private string? _containerName;

    // card microplumberd-testing-store-port-race: this store's port, held machine-wide for its lifetime (see PortLock), and
    // the id of the container IT created. Removal and restart go by that id — never by name: the name is derived from the
    // port, so another process's store can carry it.
    private PortLock? _portLock;
    private string? _containerId;
    public string ContainerName => _containerName ?? $"eventstore-mem-{httpPort}";
    public int HttpPort => httpPort;

    /// <summary>
    /// The container named EXACTLY <see cref="ContainerName"/>. A substring match would make
    /// <c>eventstore-mem-2701</c> find <c>eventstore-mem-27010</c> — and dispose a neighbour's store.
    /// </summary>
    private async Task<ContainerListResponse?> GetEventStoreContainer()
    {
        var containers = await client.Containers.ListContainersAsync(new ContainersListParameters()
        {
            All = true,
            Filters = new Dictionary<string, IDictionary<string, bool>>
            {
                ["name"] = new Dictionary<string, bool> { [$"^/{ContainerName}$"] = true }
            }
        });
        return containers.FirstOrDefault(x => x.Names.Any(n => n == "/" + ContainerName));
    }

    public async Task<bool> Stop()
    {
        // This server's own container only (by id) — a name can be another process's store.
        if (_containerId != null)
        {
            var data = await client.Containers.InspectContainerAsync(_containerId);
            if (data.State.Running)
            {
                await client.Containers.StopContainerAsync(data.ID, new ContainerStopParameters());
                return true;
            }
        }

        return false;
    }

    public async Task Restart(TimeSpan delay)
    {
        if (_containerId != null)
        {
            var data = await client.Containers.InspectContainerAsync(_containerId);
            if (data.State.Running)
            {
                await client.Containers.StopContainerAsync(data.ID, new ContainerStopParameters());
                await Task.Delay(delay);
                await client.Containers.StartContainerAsync(data.ID, new ContainerStartParameters());
                await Task.Delay(10000);
                return;
            }
        }

        throw new InvalidOperationException("No event-store container found");

    }

    public Task<EventStoreServer> StartInDocker(bool wait = true, bool inMemory = true) =>
        StartInDocker(wait, inMemory, DefaultReadyTimeout);

    /// <param name="readyTimeout">
    /// How long to wait for KurrentDB to answer. If it does not, the container is removed before the
    /// exception propagates: a caller whose <c>try/finally</c> starts after this call never gets to dispose it.
    /// </param>
    public async Task<EventStoreServer> StartInDocker(bool wait, bool inMemory, TimeSpan readyTimeout)
    {
        Interlocked.Increment(ref _starting);
        try { return await StartAndWait(wait, inMemory, readyTimeout); }
        finally { Interlocked.Decrement(ref _starting); }
    }

    private async Task<EventStoreServer> StartAndWait(bool wait, bool inMemory, TimeSpan readyTimeout)
    {
        await ReapOrphans(client);

        var container = await GetEventStoreContainer();
        if (container != null && container.ID != _containerId)
        {
            // A container with this name that THIS server did not create: another live process's store (refused, named —
            // restarting it would wipe it, removing it would pull it from under that process), or a dead owner's (reaped).
            if (!container.Labels.TryGetValue(OwnerLabel, out var owner) || !IsDeadOwnerOnThisMachine(owner))
                throw new InvalidOperationException(
                    $"Container '{ContainerName}' ({container.ID[..12]}) belongs to another process ({owner ?? "no owner label"}); " +
                    "it was neither restarted nor removed. Two processes chose the same store port.");
            await client.Containers.RemoveContainerAsync(container.ID, new ContainerRemoveParameters { Force = true, RemoveVolumes = true });
            container = null;
        }
        if (!inMemory && container != null)
        {
            await client.Containers.RemoveContainerAsync(container.ID, new ContainerRemoveParameters() { RemoveVolumes = true});
            container = null;
        }

        Undisposed[this] = 0;
        try
        {
            if (container == null)
            {
                var response = await client.Containers.CreateContainerAsync(new CreateContainerParameters()
                {
                    Image = "docker.kurrent.io/kurrent-latest/kurrentdb:latest",
                    Env = new List<string>()
                    {
                        "KURRENTDB_RUN_PROJECTIONS=All",
                        "KURRENTDB_START_STANDARD_PROJECTIONS=true",
                        "KURRENTDB_INSECURE=true",
                        "KURRENTDB_ENABLE_ATOM_PUB_OVER_HTTP=true",
                        $"KURRENTDB_MEM_DB={inMemory.ToString().ToLower()}",
                    },
                    Name = ContainerName,
                    Labels = new Dictionary<string, string> { [Label] = "eventstore", [OwnerLabel] = CurrentOwner },
                    HostConfig = new HostConfig()
                    {
                        PortBindings = new Dictionary<string, IList<PortBinding>>()
                        {
                            { $"2113", new List<PortBinding>() { new PortBinding() { HostPort = $"{httpPort}", HostIP = "0.0.0.0" } }}
                        }

                    },
                    Volumes = new Dictionary<string, EmptyStruct>() { },
                    ExposedPorts = new Dictionary<string, EmptyStruct>()
                    {
                        { "2113", default},
                    }
                });
                _containerId = response.ID;
                await client.Containers.StartContainerAsync(response.ID, new ContainerStartParameters());
            }
            else
            {
                var data = await client.Containers.InspectContainerAsync(container.ID);
                if (data.State.Running)
                    await client.Containers.RestartContainerAsync(data.ID, new ContainerRestartParameters());
                else
                    await client.Containers.StartContainerAsync(data.ID, new ContainerStartParameters());
            }

            // Health THEN a gRPC round trip (WaitUntilReady), on a budget that grows with the stores starting
            // alongside this one — read now, when they are all competing for the host.
            await WaitReady(this, this.GetEventStoreSettings(), ReadyBudget(readyTimeout, Volatile.Read(ref _starting)));
        }
        catch (Exception started)
        {
            // The START failure is the one to see (dev-clean, 2026-10-18): a cleanup that throws too (Docker 409 "removal
            // already in progress") replaced it, and the first failure was lost. Cleanup's failure rides along, never instead.
            // Both visible (rev-reach 9de418b9d m2): Data is printed by neither xUnit nor ToString. The start failure FIRST.
            try { await Cleanup(); }
            catch (Exception cleanup)
            {
                throw new AggregateException($"Store '{ContainerName}' failed to start, and removing its container failed too.",
                    started, cleanup);
            }
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(started).Throw();
            throw; // unreachable
        }

        return this;
    }

    /// <summary>
    /// Removes the containers of THIS machine's processes that no longer exist — a run that was killed
    /// (OOM, timeout, Ctrl+C) cannot dispose anything. Only containers carrying <see cref="Label"/> are
    /// considered; a live owner, another machine's owner, or an owner whose liveness cannot be read is left alone.
    /// </summary>
    /// <returns>The names of the containers removed.</returns>
    public static async Task<IReadOnlyList<string>> ReapOrphans()
    {
        using var docker = new DockerClientConfiguration().CreateClient();
        return await ReapOrphans(docker);
    }

    private static async Task<IReadOnlyList<string>> ReapOrphans(DockerClient docker)
    {
        var labelled = await docker.Containers.ListContainersAsync(new ContainersListParameters
        {
            All = true,
            Filters = new Dictionary<string, IDictionary<string, bool>>
            {
                ["label"] = new Dictionary<string, bool> { [Label] = true }
            }
        });

        var removed = new List<string>();
        foreach (var container in labelled)
        {
            if (!container.Labels.TryGetValue(OwnerLabel, out var owner) || !IsDeadOwnerOnThisMachine(owner))
                continue;

            try
            {
                await docker.Containers.RemoveContainerAsync(container.ID, new ContainerRemoveParameters { Force = true, RemoveVolumes = true });
                removed.Add(container.Names.FirstOrDefault()?.TrimStart('/') ?? container.ID);
            }
            catch (DockerContainerNotFoundException)
            {
                // Another process reaped it first.
            }
        }

        return removed;
    }

    internal static string OwnerOf(int pid, DateTime processStart) =>
        $"{Environment.MachineName}/{pid}/{processStart.ToUniversalTime().Ticks}";

    /// <summary>
    /// True only when the owner is on this machine and its process is gone — or the pid now belongs to a
    /// different process (another start time). Anything uncertain is "alive": a leaked container costs
    /// memory, a wrongly reaped one breaks someone's running test.
    /// </summary>
    internal static bool IsDeadOwnerOnThisMachine(string owner)
    {
        var parts = owner.Split('/');
        if (parts.Length != 3 || parts[0] != Environment.MachineName) return false;
        if (!int.TryParse(parts[1], out var pid) || !long.TryParse(parts[2], out var startTicks)) return false;

        try
        {
            using var process = Process.GetProcessById(pid);
            return process.StartTime.ToUniversalTime().Ticks != startTicks;
        }
        catch (ArgumentException)
        {
            return true; // no such process
        }
        catch (Exception)
        {
            return false; // exists, but cannot be read: not ours to judge
        }
    }

    private static void RemoveUndisposed()
    {
        foreach (var server in Undisposed.Keys)
        {
            try { server.Cleanup().AsTask().Wait(TimeSpan.FromSeconds(10)); }
            catch { /* process is exiting; the next start reaps what is left */ }
        }
    }

    async ValueTask Cleanup()
    {
        // ONLY the container this server created, by its id: a name lookup here removed another process's live store.
        if (_containerId != null)
        {
            try
            {
                await RemoveContainer(this, client, _containerId);
            }
            catch (DockerContainerNotFoundException)
            {
            }
            _containerId = null;
        }

        Undisposed.TryRemove(this, out _);
        Interlocked.Exchange(ref _portLock, null)?.Dispose();
    }

    public void Dispose()
    {
        Task.Run(Cleanup).Wait();
    }

    public ValueTask DisposeAsync()
    {
        return Cleanup();
    }
}
