using FluentAssertions;
using KurrentDB.Client;
using MicroPlumberd.Testing;
using MicroPlumberd.Tests.Utils;
using MicroPlumberd.Tests.App.Domain;

namespace MicroPlumberd.Tests.Integration.Services;

/// <summary>
/// Issue #27, the sibling of #25: a process manager is loaded with <c>plumber.Rehydrate(manager, "{Name}-{id}")</c> — exactly
/// what <c>ProcessManagerClient.GetManager</c> does — not with <c>Get</c>, so #26 never reached it. Rehydrate counted DELIVERED
/// events; an event of a type this build does not map (a newer release's, after a rollback) was skipped for state AND for
/// version. The version is the STREAM REVISION of the last event read, unknown events included — the #26 rule.
/// Driven through a real process manager (XooProcessManager), never a hand-built model.
/// </summary>
[TestCategory("Integration")]
public class ProcessManagerVersionTests : IClassFixture<EventStoreServer>
{
    private readonly IPlumber _plumber;
    private readonly EventStoreServer _es;

    public ProcessManagerVersionTests(EventStoreServer es)
    {
        _plumber = Plumber.Create(es.GetEventStoreSettings());
        _es = es;
    }

    /// <summary>The stream GetManager reads, by the same literal.</summary>
    private static string ManagerStream(Guid id) => $"{nameof(XooProcessManager)}-{id}";

    private async Task RawAppend(string stream, StreamState state, string eventType)
    {
        await using var client = new KurrentDBClient(_es.GetEventStoreSettings());
        await client.AppendToStreamAsync(stream, state,
            [new EventData(Uuid.NewUuid(), eventType, System.Text.Encoding.UTF8.GetBytes("""{"Whatever":1}"""))]);
    }

    /// <summary>Loaded as GetManager loads it.</summary>
    private async Task<XooProcessManager> Load(Guid id)
    {
        var manager = new XooProcessManager();
        if (manager is IIdAware aware) aware.Id = id;   // as GetManager sets it
        await _plumber.Rehydrate(manager, ManagerStream(id));
        return manager;
    }

    /// <summary>
    /// The issue's shape: a known event, then one of a type this build does not know. The version is 1 (two events), and a
    /// write at that expected version — what a caller of GetManager does with Version — succeeds. Master: 0, WrongExpectedVersion.
    /// </summary>
    [Fact]
    public async Task AnUnknownEventInTheManagersStream_CountsForVersion_AndAWriteAtThatVersionSucceeds()
    {
        await _es.StartInDocker();
        var id = Guid.NewGuid();
        await _plumber.AppendEvents(ManagerStream(id), StreamState.NoStream, new FooCreated { Name = "known" });
        await RawAppend(ManagerStream(id), StreamState.StreamExists, "EventFromANewerRelease");

        var manager = await Load(id);

        manager.Version.Should().Be(1, "two events in the stream — the unknown one counts for version");
        var write = () => _plumber.AppendEvents(ManagerStream(id), (ulong)manager.Version, new FooCreated { Name = "next" });
        await write.Should().NotThrowAsync<WrongExpectedVersionException>();
    }

    /// <summary>
    /// A manager whose stream holds ONLY events this build does not know EXISTS: version 0, not -1. The executor reads
    /// `Version &lt; 0` as "never created" and silently drops the event that reached it.
    /// </summary>
    [Fact]
    public async Task AStreamOfOnlyUnknownEvents_IsAManagerThatExists()
    {
        await _es.StartInDocker();
        var id = Guid.NewGuid();
        await RawAppend(ManagerStream(id), StreamState.NoStream, "EventFromANewerRelease");

        (await Load(id)).Version.Should().Be(0, "the stream exists: its one event is revision 0");
    }

    /// <summary>
    /// THE PLAUSIBLE WRONG FIX's row: "count the unknown events too" (Increase() for every event) agrees with the revision only
    /// while the stream is read whole from revision 0. With its head truncated (TruncateBefore = 2, three events written), the
    /// first event read is revision 2: the revision is 2, a count is 0 — and a write at a counted version fails.
    /// </summary>
    [Fact]
    public async Task AManagersStreamWithItsHeadTruncated_HasTheRevisionAsVersion_NotACount()
    {
        await _es.StartInDocker();
        var id = Guid.NewGuid();
        await _plumber.AppendEvents(ManagerStream(id), StreamState.NoStream,
            new FooCreated { Name = "a" }, new FooCreated { Name = "b" }, new FooCreated { Name = "c" });
        await using (var client = new KurrentDBClient(_es.GetEventStoreSettings()))
            await client.SetStreamMetadataAsync(ManagerStream(id), StreamState.Any, new StreamMetadata { TruncateBefore = StreamPosition.FromInt64(2) });

        var manager = await Load(id);

        manager.Version.Should().Be(2, "the version is the revision of the last event read, not how many were read");
        var write = () => _plumber.AppendEvents(ManagerStream(id), (ulong)manager.Version, new FooCreated { Name = "d" });
        await write.Should().NotThrowAsync<WrongExpectedVersionException>();
    }
}
