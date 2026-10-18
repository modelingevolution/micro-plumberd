using System.Text.Json.Nodes;
using FluentAssertions;
using KurrentDB.Client;
using MicroPlumberd.Api;
using MicroPlumberd.Services;
using MicroPlumberd.Testing;
using MicroPlumberd.Tests.App.SnapshotDomain;
using MicroPlumberd.Tests.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace MicroPlumberd.Tests.Integration;

/// <summary>
/// Issue #28. A snapshot written by a build before #26 recorded a COUNT of the events that build could map as its version.
/// Over a stream holding events it did not know, that version is LOW; loading from it read on at version + 1 and RE-FOLDED
/// events already in the snapshot's state. These rows write such a snapshot the way that build wrote it — the same event,
/// the same metadata, without the marker this fix adds — over a real stream, and load through Plumber.Get.
/// TallyAggregate's state shows a re-fold: "abb" / Count 3, never a last-write-wins value that hides it.
/// </summary>
[TestCategory("Integration")]
public class SnapshotRevisionTests : IClassFixture<EventStoreServer>
{
    private readonly IPlumberApi _plumber;
    private readonly EventStoreServer _es;

    public SnapshotRevisionTests(EventStoreServer es)
    {
        // Through DI, as an application gets it: a snapshot policy (ISnapshotPolicy<> → AttributeSnaphotPolicy<>, registered by
        // AddPlumberd) is what makes Get read a snapshot at all — Plumber.Create alone has none.
        _plumber = new ServiceCollection().AddPlumberd(es.GetEventStoreSettings()).BuildServiceProvider()
            .GetRequiredService<IPlumberInstance>();
        _es = es;
    }

    private string Stream(Guid id) => _plumber.Config.Conventions.GetStreamIdConvention(null!, typeof(TallyAggregate), id);
    private string SnapshotStream(Guid id) => _plumber.Config.Conventions.GetStreamIdSnapshotConvention(null!, typeof(TallyAggregate.TallyState), id);

    private async Task Append(Guid id, params object[] events) =>
        await _plumber.AppendEvents(Stream(id), StreamState.Any, events);

    private async Task AppendUnknown(Guid id)
    {
        await using var client = new KurrentDBClient(_es.GetEventStoreSettings());
        await client.AppendToStreamAsync(Stream(id), StreamState.Any,
            [new EventData(Uuid.NewUuid(), "EventFromANewerRelease", "{}"u8.ToArray(), "{}"u8.ToArray())]);
    }

    /// <summary>
    /// A snapshot as a build before #28 wrote it: written through the real AppendSnapshot, then re-appended as the stream's
    /// latest snapshot with the SnapshotVersionIsRevision marker removed — the same type, data and metadata otherwise.
    /// </summary>
    private async Task LegacySnapshot(Guid id, TallyAggregate.TallyState state, long version)
    {
        await _plumber.AppendSnapshot(state, id, version);
        await using var client = new KurrentDBClient(_es.GetEventStoreSettings());
        var written = (await client.ReadStreamAsync(Direction.Backwards, SnapshotStream(id), StreamPosition.End, maxCount: 1).ToListAsync())[0].Event;
        var metadata = JsonNode.Parse(written.Metadata.Span)!.AsObject();
        metadata.Remove("SnapshotVersionIsRevision").Should().BeTrue("the real AppendSnapshot writes the marker");
        await client.AppendToStreamAsync(SnapshotStream(id), StreamState.Any,
            [new EventData(Uuid.NewUuid(), written.EventType, written.Data, System.Text.Encoding.UTF8.GetBytes(metadata.ToJsonString()))]);
        (await _plumber.GetSnapshot(id, typeof(TallyAggregate.TallyState)))!.VersionIsRevision.Should().BeFalse("precondition: a legacy snapshot");
    }

    /// <summary>
    /// THE ISSUE. The old build saw [a, (unknown), b] as two events and snapshotted {ab, 2} at version 1 — the revision is 2.
    /// Reading on from 2 re-folds b: "abb". Rebuilt from the stream: "ab", the unknown skipped, version 2.
    /// </summary>
    [Fact]
    public async Task ALegacySnapshotOverAnUnknownEvent_DoesNotReFold()
    {
        await _es.StartInDocker();
        var id = Guid.NewGuid();
        await Append(id, new TallyAdded { Mark = "a" });
        await AppendUnknown(id);
        await Append(id, new TallyAdded { Mark = "b" });
        await LegacySnapshot(id, new TallyAggregate.TallyState { Count = 2, Seen = "ab" }, version: 1);

        var tally = await _plumber.Get<TallyAggregate>(id);

        tally.State.Seen.Should().Be("ab");
        tally.State.Count.Should().Be(2);
        tally.Version.Should().Be(2);
    }

    /// <summary>
    /// The UPGRADE case — and the plausible wrong fix's blind spot. The old build did not map TallyNoted (added later); it
    /// snapshotted [a, x, b] as {ab, 2} at version 1. THIS build maps TallyNoted, so "count this build's mapped events to
    /// find where the snapshot ends" lands after x — and re-folds b. Rebuilt: "axb", 3 — the state this build folds.
    /// </summary>
    [Fact]
    public async Task ALegacySnapshotOverATypeThisBuildNowMaps_IsRebuilt_NotPositionedByThisBuildsCount()
    {
        await _es.StartInDocker();
        var id = Guid.NewGuid();
        await Append(id, new TallyAdded { Mark = "a" }, new TallyNoted { Mark = "x" }, new TallyAdded { Mark = "b" });
        await LegacySnapshot(id, new TallyAggregate.TallyState { Count = 2, Seen = "ab" }, version: 1);

        var tally = await _plumber.Get<TallyAggregate>(id);

        tally.State.Seen.Should().Be("axb");
        tally.State.Count.Should().Be(3);
        tally.Version.Should().Be(2);
    }

    /// <summary>
    /// A legacy snapshot that is RIGHT — the event after its version was written after it — is still USED (not every legacy
    /// snapshot is rebuilt: that would cost every library user a full replay). The snapshot's state is a sentinel {S, 100}:
    /// used → "Sb" / 101; rebuilt → "ab" / 2.
    /// </summary>
    [Fact]
    public async Task ALegacySnapshotWhoseNextEventCameAfterIt_IsUsed()
    {
        await _es.StartInDocker();
        var id = Guid.NewGuid();
        await Append(id, new TallyAdded { Mark = "a" });
        await LegacySnapshot(id, new TallyAggregate.TallyState { Count = 100, Seen = "S" }, version: 0);
        await Append(id, new TallyAdded { Mark = "b" });

        var tally = await _plumber.Get<TallyAggregate>(id);

        tally.State.Seen.Should().Be("Sb");
        tally.State.Count.Should().Be(101);
        tally.Version.Should().Be(1);
    }

    /// <summary>
    /// A snapshot this build writes carries the marker: its version IS the revision, so it is used even where the order check
    /// alone would doubt it — here the event after it was written BEFORE it (another writer between a save and its snapshot).
    /// Sentinel state: used → "Sb" / 101 (over [a, (unknown), b], version 1 is the revision of the unknown event).
    /// </summary>
    [Fact]
    public async Task ASnapshotThisBuildWrote_IsUsed_EvenWhenTheNextEventPrecedesIt()
    {
        await _es.StartInDocker();
        var id = Guid.NewGuid();
        await Append(id, new TallyAdded { Mark = "a" });
        await AppendUnknown(id);
        await Append(id, new TallyAdded { Mark = "b" });
        await _plumber.AppendSnapshot(new TallyAggregate.TallyState { Count = 100, Seen = "S" }, id, 1);
        (await _plumber.GetSnapshot(id, typeof(TallyAggregate.TallyState)))!.VersionIsRevision.Should().BeTrue();

        var tally = await _plumber.Get<TallyAggregate>(id);

        tally.State.Seen.Should().Be("Sb");
        tally.State.Count.Should().Be(101);
        tally.Version.Should().Be(2);
    }

    /// <summary>
    /// Compatibility the other way: an OLDER build (before this marker) reading a snapshot THIS build wrote is unaffected — the
    /// extra metadata key is ignored. Read here exactly as the pre-marker reader reads (master 875ab10b, PlumberEngine.GetSnapshot
    /// 1305-1315): the latest event of the snapshot stream, its type the snapshot event name, its data through the serializer,
    /// Created and SnapshotVersion from the metadata — and nothing else.
    /// </summary>
    [Fact]
    public async Task AMarkedSnapshot_ReadsThroughThePreMarkerReader_Unchanged()
    {
        await _es.StartInDocker();
        var id = Guid.NewGuid();
        var state = new TallyAggregate.TallyState { Count = 7, Seen = "abcdefg" };
        await _plumber.AppendSnapshot(state, id, 6);

        await using var client = new KurrentDBClient(_es.GetEventStoreSettings());
        var e = (await client.ReadStreamAsync(Direction.Backwards, SnapshotStream(id), StreamPosition.End, maxCount: 1).ToListAsync())[0].Event;

        e.EventType.Should().Be(_plumber.Config.Conventions.SnapshotEventNameConvention(typeof(TallyAggregate.TallyState)));
        var value = _plumber.Config.SerializerFactory(typeof(TallyAggregate.TallyState)).Deserialize(null!, e.Data.Span, typeof(TallyAggregate.TallyState));
        value.Should().BeEquivalentTo(state);
        using var metadata = System.Text.Json.JsonDocument.Parse(e.Metadata);
        DateTimeOffset.Parse(metadata.RootElement.GetProperty("Created").GetString()!).Should().BeCloseTo(DateTimeOffset.Now, TimeSpan.FromMinutes(5));
        metadata.RootElement.GetProperty("SnapshotVersion").GetInt64().Should().Be(6);
        metadata.RootElement.GetProperty("SnapshotVersionIsRevision").GetBoolean().Should().BeTrue("the marker is an ADDED key the old reader skips");
    }

    /// <summary>
    /// rev-reach a3f8d0c32 (m1): "written after" is decided by the $all COMMIT POSITION, never by Created (the server's wall
    /// clock). A real store cannot plant a clock step, so the decision is isolated in PlumberEngine.WrittenAfter and fed two
    /// records whose Created order CONTRADICTS their positions — what a clock stepped back between the two writes produces.
    /// The event at version + 1 committed BEFORE the snapshot (position 100 &lt; 200) but stamped LATER (Created +1 h):
    /// it is NOT "written after" → the short legacy snapshot is not used.
    /// </summary>
    [Fact]
    public void WrittenAfter_IsDecidedByCommitPosition_NotByCreated()
    {
        var t0 = DateTime.UtcNow;
        var next = Record(position: 100, created: t0.AddHours(1));    // committed first, clock says later
        var snapshot = Record(position: 200, created: t0);            // committed second, clock says earlier
        next.Created.Should().BeAfter(snapshot.Created, "precondition: the records' clocks contradict their commit order");

        PlumberEngine.WrittenAfter(next, snapshot).Should().BeFalse("the event at v+1 committed BEFORE the snapshot, whatever its clock says");
        PlumberEngine.WrittenAfter(snapshot, next).Should().BeTrue();
    }

    private static EventRecord Record(ulong position, DateTime created) =>
        new("Tally-x", Uuid.NewUuid(), StreamPosition.FromInt64(0), new Position(position, position),
            new Dictionary<string, string>
            {
                ["type"] = "TallyAdded",
                ["created"] = new DateTimeOffset(created).ToUnixTimeMilliseconds().ToString() + "0000",
                ["content-type"] = "application/json"
            },
            ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty);
}
