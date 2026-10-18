using System.Diagnostics;
using Docker.DotNet;
using Docker.DotNet.Models;
using FluentAssertions;
using MicroPlumberd.Testing;
using MicroPlumberd.Tests.Utils;

namespace MicroPlumberd.Tests.Integration;

/// <summary>
/// card microplumberd-testing-store-port-race: a container that carries this server's name but belongs to ANOTHER LIVE
/// process is never restarted (that wiped its in-memory store) and never removed (that pulled it from under that process) —
/// not at start, not at dispose. The port lock makes such a name clash rare; these rows hold the second line of defence
/// without a race. The planted container's owner is PID 1 with its real start time: alive for as long as the machine is.
/// </summary>
[TestCategory("Integration")]
public class EventStoreServerForeignContainerTests : IAsyncLifetime
{
    private readonly DockerClient _docker = new DockerClientConfiguration().CreateClient();
    private readonly string _name = $"mp-foreign-{Guid.NewGuid():N}"[..24];
    private string _planted = "";

    public async Task InitializeAsync()
    {
        var liveForeignOwner = EventStoreServer.OwnerOf(1, Process.GetProcessById(1).StartTime);
        _planted = (await _docker.Containers.CreateContainerAsync(new CreateContainerParameters
        {
            Image = "docker.kurrent.io/kurrent-latest/kurrentdb:latest",
            Name = _name,
            Labels = new Dictionary<string, string> { [EventStoreServer.Label] = "eventstore", [EventStoreServer.OwnerLabel] = liveForeignOwner },
        })).ID;
    }

    public async Task DisposeAsync()
    {
        try { await _docker.Containers.RemoveContainerAsync(_planted, new ContainerRemoveParameters { Force = true }); }
        catch (DockerContainerNotFoundException) { }
        _docker.Dispose();
    }

    private async Task<ContainerInspectResponse?> Planted()
    {
        try { return await _docker.Containers.InspectContainerAsync(_planted); }
        catch (DockerContainerNotFoundException) { return null; }
    }

    [Fact]
    public async Task AStart_OverAnotherLiveProcesssContainerOfTheSameName_IsRefused_AndLeavesItAlone()
    {
        await using var server = new EventStoreServer(_name);

        var start = await Record.ExceptionAsync(() => server.StartInDocker());

        start.Should().BeOfType<InvalidOperationException>().Which.Message.Should()
            .Contain(_name).And.Contain("another process").And.Contain("neither restarted nor removed");
        var planted = await Planted();
        planted.Should().NotBeNull("it was not removed");
        planted!.State.Running.Should().BeFalse("nor (re)started");
    }

    [Fact]
    public async Task ADispose_NeverRemovesAContainerThisServerDidNotCreate()
    {
        var server = new EventStoreServer(_name);
        await Record.ExceptionAsync(() => server.StartInDocker());   // refused: it created nothing

        await server.DisposeAsync();

        (await Planted()).Should().NotBeNull("dispose removes only the container THIS server created, by its id");
    }
}
