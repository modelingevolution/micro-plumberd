using MicroPlumberd.Tests.App.Infrastructure;

namespace MicroPlumberd.Tests.App.Domain;

/// <summary>
/// A FooModel variant that implements ICaughtUpHandler to track
/// whether the subscription has caught up with historical events.
/// Records the order of event deliveries vs CaughtUp calls.
/// </summary>
[OutputStream("CaughtUpFooModel_v1")]
[EventHandler]
public partial class CaughtUpFooModel(InMemoryAssertionDb assertionDb) : ICaughtUpHandler
{
    public record TimelineEntry(string Kind, object? Payload, bool IsLiveAtTime);

    /// <summary>
    /// Ordered timeline of everything that happened: "event" entries and "caught-up" markers.
    /// Each entry captures the IsLive state at the moment it was recorded.
    /// </summary>
    public List<TimelineEntry> Timeline { get; } = new();

    public InMemoryAssertionDb AssertionDb => assertionDb;
    public bool IsLive { get; private set; }
    public int CaughtUpCount { get; private set; }

    public Task CaughtUp()
    {
        IsLive = true;
        CaughtUpCount++;
        Timeline.Add(new TimelineEntry("caught-up", null, IsLive));
        return Task.CompletedTask;
    }

    private async Task Given(Metadata m, FooCreated ev)
    {
        assertionDb.Add(m, ev);
        Timeline.Add(new TimelineEntry("event", ev, IsLive));
        await Task.Delay(0);
    }

    private async Task Given(Metadata m, FooRefined ev)
    {
        assertionDb.Add(m, ev);
        Timeline.Add(new TimelineEntry("event", ev, IsLive));
        await Task.Delay(0);
    }
}
