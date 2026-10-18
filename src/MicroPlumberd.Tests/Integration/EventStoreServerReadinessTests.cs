using System.Collections.Concurrent;
using FluentAssertions;
using MicroPlumberd.Testing;
using MicroPlumberd.Tests.Utils;

namespace MicroPlumberd.Tests.Integration;

/// <summary>
/// The readiness budget is WIRED, not only computed: stores started at once each wait with a budget that counts
/// the others (StoreReadinessTests pins ReadyBudget itself). Two real stores, started together, each removed.
/// </summary>
[TestCategory("Integration")]
public class EventStoreServerReadinessTests
{
    [Fact]
    public async Task StoresStartedTogether_WaitWithABudgetThatCountsEachOther()
    {
        var budgets = new ConcurrentBag<TimeSpan>();
        var real = EventStoreServer.WaitReady;
        await using var a = EventStoreServer.Create();
        await using var b = EventStoreServer.Create();
        // Only these two stores' budgets: other classes start stores through the same static seam in parallel.
        EventStoreServer.WaitReady = async (server, settings, budget) =>
        {
            if (ReferenceEquals(server, a) || ReferenceEquals(server, b)) budgets.Add(budget);
            await real(server, settings, budget);
        };
        try
        {
            await Task.WhenAll(a.StartInDocker(), b.StartInDocker());
        }
        finally
        {
            EventStoreServer.WaitReady = real;
        }

        budgets.Should().HaveCount(2);
        budgets.Max().Should().BeGreaterThan(TimeSpan.FromSeconds(30),
            "the second store's wait began while the first was still starting, so its budget counts both");
    }
}
