using System.Diagnostics;
using Docker.DotNet;
using Docker.DotNet.Models;
using FluentAssertions;
using MicroPlumberd.Testing;
using MicroPlumberd.Tests.Utils;

namespace MicroPlumberd.Tests.Integration;

/// <summary>
/// No <see cref="EventStoreServer"/> container outlives its test (epic-089 status §296: 47 leaked KurrentDB
/// containers, ~0.8 GB each, OOM-killed the host). Each test counts the containers by NAME before and after,
/// on the shared daemon, and touches only containers it created itself.
/// </summary>
[TestCategory("Integration")]
public class EventStoreServerLeakTests : IDisposable
{
    private readonly DockerClient _docker = new DockerClientConfiguration().CreateClient();
    private readonly string _tag = $"mp-leak-{Guid.NewGuid():N}"[..20];
    private readonly List<string> _planted = new();

    [Fact]
    public async Task AStartReapsADeadProcesssContainer_LabelsItsOwn_AndDisposeRemovesIt()
    {
        var orphan = await PlantAsync($"{_tag}-orphan", owner: await DeadOwnerAsync());
        var server = EventStoreServer.Create($"{_tag}-dispose");
        (await Count(server.ContainerName)).Should().Be(0);

        await server.StartInDocker();
        (await Count(server.ContainerName)).Should().Be(1);
        (await Count(orphan)).Should().Be(0, "starting a server reaps what a killed run left behind");

        var own = (await _docker.Containers.InspectContainerAsync(server.ContainerName)).Config.Labels;
        own[EventStoreServer.OwnerLabel].Should().Be(
            $"{Environment.MachineName}/{Environment.ProcessId}/{Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks}",
            "a container whose owner cannot be read back as THIS process would be reaped from under it, or never");

        await server.DisposeAsync();
        (await Count(server.ContainerName)).Should().Be(0, "dispose removes the container, not just stops it");
    }

    /// <summary>
    /// The path that leaked: <c>var es = …; await es.StartInDocker(); try { … } finally { dispose }</c>. The
    /// container exists before readiness is known; when the wait throws (a loaded host takes &gt; 30 s), the
    /// caller's finally was never entered. The server must remove what it created.
    /// </summary>
    [Fact]
    public async Task AStartThatNeverBecomesReadyRemovesTheContainerItCreated()
    {
        var server = EventStoreServer.Create($"{_tag}-unready");

        var start = () => server.StartInDocker(true, true, TimeSpan.FromMilliseconds(1));

        await start.Should().ThrowAsync<Exception>();
        (await Count(server.ContainerName)).Should().Be(0, "nobody else holds it to dispose");
    }

    /// <summary>A killed process disposes nothing: the next start on this machine removes its containers.</summary>
    [Fact]
    public async Task AContainerOfADeadProcessIsReaped_ALiveOnesAndAnUnlabelledOneAreNot()
    {
        var dead = await PlantAsync($"{_tag}-dead", owner: await DeadOwnerAsync());
        var live = await PlantAsync($"{_tag}-live",
            owner: $"{Environment.MachineName}/{Environment.ProcessId}/{Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks}");
        var otherMachine = await PlantAsync($"{_tag}-elsewhere", owner: "some-other-machine/1/1");
        var unlabelled = await PlantAsync($"{_tag}-foreign", owner: null);

        var removed = await EventStoreServer.ReapOrphans();

        removed.Should().Contain(dead);
        (await Count(dead)).Should().Be(0, "its owner is gone");
        (await Count(live)).Should().Be(1, "its owner — this process — is running");
        (await Count(otherMachine)).Should().Be(1, "another machine's pid says nothing about this one");
        (await Count(unlabelled)).Should().Be(1, "not ours: the daemon is shared");
    }

    /// <summary>
    /// The lookup used to be a SUBSTRING match, so disposing <c>…-1</c> removed a neighbour named <c>…-12</c>.
    /// </summary>
    [Fact]
    public async Task DisposingAServerDoesNotRemoveANeighbourWhoseNameStartsWithItsName()
    {
        var neighbour = await PlantAsync($"{_tag}-12", owner: null);
        var server = EventStoreServer.Create($"{_tag}-1");

        await server.DisposeAsync();

        (await Count(neighbour)).Should().Be(1);
    }

    private async Task<int> Count(string name)
    {
        var all = await _docker.Containers.ListContainersAsync(new ContainersListParameters { All = true });
        return all.Count(c => c.Names.Contains("/" + name));
    }

    /// <summary>A created (never started) container: no memory, no port; removed by Dispose if still there.</summary>
    private async Task<string> PlantAsync(string name, string? owner)
    {
        var labels = owner is null
            ? new Dictionary<string, string> { ["mp-leak-test"] = "planted" }
            : new Dictionary<string, string> { [EventStoreServer.Label] = "eventstore", [EventStoreServer.OwnerLabel] = owner };

        await _docker.Containers.CreateContainerAsync(new CreateContainerParameters
        {
            Image = "docker.kurrent.io/kurrent-latest/kurrentdb:latest",
            Name = name,
            Labels = labels
        });
        _planted.Add(name);
        return name;
    }

    /// <summary>
    /// The pid of a process that has exited. Its start time is not read: `true` can be gone before
    /// <see cref="Process.StartTime"/> is (that read throws), and a pid with no process is dead whatever the ticks.
    /// </summary>
    private static async Task<string> DeadOwnerAsync()
    {
        using var process = Process.Start(new ProcessStartInfo("true") { UseShellExecute = false })!;
        await process.WaitForExitAsync();
        return $"{Environment.MachineName}/{process.Id}/0";
    }

    public void Dispose()
    {
        foreach (var name in _planted)
        {
            try { _docker.Containers.RemoveContainerAsync(name, new ContainerRemoveParameters { Force = true }).GetAwaiter().GetResult(); }
            catch (DockerContainerNotFoundException) { }
        }
        _docker.Dispose();
    }
}
