using FluentAssertions;
using MicroPlumberd.Testing;
using MicroPlumberd.Tests.Utils;

namespace MicroPlumberd.Tests.Integration;

/// <summary>
/// A failed start removes its container — and if that removal ALSO fails (two processes removing one container gave Docker
/// 409 "removal already in progress", 2026-10-18), the START failure is still the one thrown; the cleanup failure rides
/// along in Data. It used to replace it, and the first failure was lost (dev-clean's observation).
/// </summary>
[TestCategory("Integration")]
public class EventStoreServerStartFailureTests
{
    private sealed class NotReady() : Exception("the store did not become ready (planted)");

    [Fact]
    public async Task AStartThatFails_AndWhoseCleanupAlsoFails_ThrowsTheStartFailure()
    {
        await using var server = EventStoreServer.Create();
        var ready = EventStoreServer.WaitReady;
        var remove = EventStoreServer.RemoveContainer;
        // Only THIS server's store: the suite runs other classes' stores in parallel through the same static seams.
        EventStoreServer.WaitReady = (s, settings, budget) =>
            ReferenceEquals(s, server) ? Task.FromException(new NotReady()) : ready(s, settings, budget);
        EventStoreServer.RemoveContainer = async (s, docker, id) =>
        {
            await remove(s, docker, id);   // the container really goes
            if (ReferenceEquals(s, server)) throw new InvalidOperationException("removal of container is already in progress (planted)");
        };
        try
        {
            var thrown = await Record.ExceptionAsync(() => server.StartInDocker());

            thrown.Should().BeOfType<NotReady>("the start failure is the first failure, and it is the one thrown");
            thrown!.Data["MicroPlumberd.Testing.CleanupFailure"].Should().BeOfType<string>()
                .Which.Should().Contain("already in progress", "the cleanup failure rides along, not instead");
        }
        finally
        {
            EventStoreServer.WaitReady = ready;
            EventStoreServer.RemoveContainer = remove;
        }
    }
}
