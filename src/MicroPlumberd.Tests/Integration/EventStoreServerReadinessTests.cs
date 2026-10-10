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
        EventStoreServer.WaitReady = async (settings, budget) => { budgets.Add(budget); await real(settings, budget); };
        try
        {
            await using var a = EventStoreServer.Create();
            await using var b = EventStoreServer.Create();
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
