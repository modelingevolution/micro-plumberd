using FluentAssertions;
using MicroPlumberd.Services.Cron;

namespace MicroPlumberd.Tests.Unit;

public class ScheduledJobSortedSetTests
{
    [Fact]
    public void GuidIsGuid()
    {
        var n = DateTime.Now;
        var jobDefinitionId = Guid.NewGuid();
        var s  = new ScheduledJob(jobDefinitionId, n);
        s.JobDefinitionId.Should().Be(jobDefinitionId);
    }

    [Fact]
    public void GetPendingItems()
    {
        SortedSet<ScheduledJob> items = new SortedSet<ScheduledJob>();
        var n = DateTime.Now;
        items.Add(new ScheduledJob(Guid.NewGuid(), n));
        items.Add(new ScheduledJob(Guid.NewGuid(), n.AddMinutes(1)));
        items.Add(new ScheduledJob(Guid.NewGuid(), n.AddMinutes(2)));
        items.Add(new ScheduledJob(Guid.NewGuid(), n.AddMinutes(2)));

        var pending = items.GetPending(n.AddSeconds(30));
        pending.Should().HaveCount(1);
    }
    [Fact]
    public void GetEmptyPendingItems()
    {
        SortedSet<ScheduledJob> items = new SortedSet<ScheduledJob>();
        var n = DateTime.Now;
        items.Add(new ScheduledJob(Guid.NewGuid(), n));
        items.Add(new ScheduledJob(Guid.NewGuid(), n.AddMinutes(1)));
        items.Add(new ScheduledJob(Guid.NewGuid(), n.AddMinutes(2)));

        var pending = items.GetPending(n.AddSeconds(-30));
        pending.Should().HaveCount(0);
    }
}