using FluentAssertions;
using MicroPlumberd.Testing;
using MicroPlumberd.Tests.Utils;

namespace MicroPlumberd.Tests.Integration;

/// <summary>
/// A failed start removes its container — and if that removal ALSO fails (two processes removing one container gave Docker
/// 409 "removal already in progress", 2026-10-18), the START failure is still the one thrown; the cleanup failure rides
/// along, both printed (an AggregateException, start failure first). It used to replace it, and the first failure was lost
/// (dev-clean's observation); then it rode in Data, which nothing prints (rev-reach 9de418b9d m2).
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

            var both = thrown.Should().BeOfType<AggregateException>().Subject;
            both.InnerExceptions.Should().HaveCount(2);
            both.InnerExceptions[0].Should().BeOfType<NotReady>("the start failure is the first failure, and it comes first");
            both.InnerExceptions[1].Message.Should().Contain("already in progress", "the cleanup failure rides along, not instead");
            both.ToString().Should().Contain("did not become ready").And.Contain("already in progress",
                "both are printed — Data was printed by neither xUnit nor ToString");
        }
        finally
        {
            EventStoreServer.WaitReady = ready;
            EventStoreServer.RemoveContainer = remove;
        }
    }
}
