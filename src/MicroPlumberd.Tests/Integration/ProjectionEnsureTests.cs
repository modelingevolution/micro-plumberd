using System.Text.Json;
using FluentAssertions;
using KurrentDB.Client;
using MicroPlumberd.Testing;
using MicroPlumberd.Tests.App.Domain;
using MicroPlumberd.Tests.Utils;
using Xunit.Abstractions;

namespace MicroPlumberd.Tests.Integration;

/// <summary>
/// Ensuring a join projection again — every application start does — must never switch it off, and must
/// recognise an unchanged query.
/// <para>
/// Seen in production (epic-089): MicroPlumberd kept the query hash in the OUTPUT stream's metadata, written
/// while that stream was still empty; the projection's first emit into it rewrote the metadata
/// (<c>{"$acl":{}}</c>) and the hash was gone. The next ensure then read "changed" and DISABLED the projection
/// before updating it; a refused update leaves it disabled — a read model that silently stops.
/// </para>
/// <para>
/// A disable is witnessed deterministically: KurrentDB persists every definition change in
/// <c>$projections-{name}</c>, and a disabled definition carries no <c>"enabled": true</c>.
/// </para>
/// </summary>
[TestCategory("Integration")]
public class ProjectionEnsureTests(ITestOutputHelper output) : IAsyncDisposable, IDisposable
{
    private const string Name = "CaughtUpFooModel_v1"; // [OutputStream] on CaughtUpFooModel
    private readonly EventStoreServer _eventStore = new();

    [Fact]
    public async Task ASecondEnsureAfterTheFirstEmit_IsUnchanged_NeverDisables_AndLinksNothingTwice()
    {
        var settings = await StartAsync();
        var first = Plumber.Create(settings);
        (await first.TryCreateJoinProjection<CaughtUpFooModel>()).Should().BeTrue("created");
        await FirstEmitAsync(first, settings);

        var before = await LastDefinitionAsync(settings);
        var changed = await Plumber.Create(settings).TryCreateJoinProjection<CaughtUpFooModel>(); // a new start

        (await DisabledDefinitionsAfterAsync(settings, before)).Should().BeEmpty("an ensure must never disable the projection");
        (await StatusAsync(settings)).Should().Be("Running");
        changed.Should().BeFalse("the query did not change, and its hash must survive the projection's own writes");

        await first.SaveNew(FooAggregate.Open("second"));
        await WaitUntil(async () => await LinkCount(settings) >= 2, "the projection still links after the ensure");
        await Task.Delay(2000);
        (await LinkCount(settings)).Should().Be(2, "an ensure must not make the projection link history again");

        (await Plumber.Create(settings).TryCreateJoinProjection<CaughtUpFooModel>()).Should().BeFalse("and on every later start");
    }

    /// <summary>Production's SubscriptionUsageReadModel: created by an older MicroPlumberd, its hash erased.</summary>
    [Fact]
    public async Task AProjectionWhoseHashWasErased_IsUpdatedWithoutBeingDisabled_AndRemembersItAfterwards()
    {
        var settings = await StartAsync();
        var first = Plumber.Create(settings);
        await first.TryCreateJoinProjection<CaughtUpFooModel>();
        await FirstEmitAsync(first, settings);
        await EraseHashesAsync(settings, legacy: null);

        var before = await LastDefinitionAsync(settings);
        var changed = await Plumber.Create(settings).TryCreateJoinProjection<CaughtUpFooModel>();

        (await DisabledDefinitionsAfterAsync(settings, before)).Should().BeEmpty("unknown is not a reason to switch it off");
        (await StatusAsync(settings)).Should().Be("Running");
        changed.Should().BeTrue("with no hash anywhere the query is pushed once");
        (await Plumber.Create(settings).TryCreateJoinProjection<CaughtUpFooModel>()).Should().BeFalse("and then it is remembered");
    }

    /// <summary>Production's other projections: hash only at the legacy location — recognised, not updated.</summary>
    [Fact]
    public async Task AHashAtTheLegacyLocation_IsRecognised_NotUpdated_AndMovedToItsOwnStream()
    {
        var settings = await StartAsync();
        var first = Plumber.Create(settings);
        await first.TryCreateJoinProjection<CaughtUpFooModel>();
        await FirstEmitAsync(first, settings);
        string? hash;
        await using (var client = new KurrentDBClient(settings))
            hash = await ProjectionQueryHash.ReadAsync(client, Name);
        hash.Should().NotBeNull();
        await EraseHashesAsync(settings, legacy: hash);

        var before = await LastDefinitionAsync(settings);
        var changed = await Plumber.Create(settings).TryCreateJoinProjection<CaughtUpFooModel>();

        changed.Should().BeFalse();
        (await LastDefinitionAsync(settings)).Should().Be(before, "an unchanged query is not pushed again");
        await using (var client = new KurrentDBClient(settings))
            (await client.GetStreamMetadataAsync(ProjectionQueryHash.StreamFor(Name))).Metadata.CustomMetadata
                .Should().NotBeNull("the hash now lives where no projection writes");
    }

    private async Task<KurrentDBClientSettings> StartAsync()
    {
        await _eventStore.StartInDocker();
        return _eventStore.GetEventStoreSettings();
    }

    /// <summary>The first event of a type the projection links: its first emit into the until-now empty output stream.</summary>
    private static async Task FirstEmitAsync(IPlumber plumber, KurrentDBClientSettings settings)
    {
        await plumber.SaveNew(FooAggregate.Open("first"));
        await WaitUntil(async () => await LinkCount(settings) >= 1, "the projection linked the first event");
    }

    /// <summary>The state an older MicroPlumberd leaves: no own hash stream; the output stream's metadata as given.</summary>
    private static async Task EraseHashesAsync(KurrentDBClientSettings settings, string? legacy)
    {
        await using var client = new KurrentDBClient(settings);
        await client.SetStreamMetadataAsync(ProjectionQueryHash.StreamFor(Name), StreamState.Any, new KurrentDB.Client.StreamMetadata());
        await client.SetStreamMetadataAsync(Name, StreamState.Any, new KurrentDB.Client.StreamMetadata(
            customMetadata: legacy is null ? null : JsonDocument.Parse($"{{\"mp_query_hash\":\"{legacy}\"}}")));
        (await ProjectionQueryHash.ReadAsync(client, Name)).Should().Be(legacy);
    }

    private static async Task<long> LastDefinitionAsync(KurrentDBClientSettings settings)
    {
        await using var client = new KurrentDBClient(settings);
        var last = client.ReadStreamAsync(Direction.Backwards, "$projections-" + Name, StreamPosition.End, maxCount: 1);
        await foreach (var e in last) return e.Event.EventNumber.ToInt64();
        return -1;
    }

    private async Task<long[]> DisabledDefinitionsAfterAsync(KurrentDBClientSettings settings, long after)
    {
        await using var client = new KurrentDBClient(settings);
        var disabled = new List<long>();
        await foreach (var e in client.ReadStreamAsync(Direction.Forwards, "$projections-" + Name, StreamPosition.FromInt64(after + 1)))
        {
            var enabled = JsonDocument.Parse(e.Event.Data).RootElement.TryGetProperty("enabled", out var v) && v.GetBoolean();
            output.WriteLine($"$projections-{Name} #{e.Event.EventNumber}: enabled={enabled}");
            if (!enabled) disabled.Add(e.Event.EventNumber.ToInt64());
        }
        return [.. disabled];
    }

    private static async Task<string> StatusAsync(KurrentDBClientSettings settings) =>
        (await new KurrentDBProjectionManagementClient(settings).GetStatusAsync(Name))!.Status;

    private static async Task<int> LinkCount(KurrentDBClientSettings settings)
    {
        await using var client = new KurrentDBClient(settings);
        var read = client.ReadStreamAsync(Direction.Forwards, Name, StreamPosition.Start, resolveLinkTos: false);
        if (await read.ReadState == ReadState.StreamNotFound) return 0;
        var n = 0;
        await foreach (var _ in read) n++;
        return n;
    }

    private static async Task WaitUntil(Func<Task<bool>> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return;
            await Task.Delay(250);
        }
        throw new TimeoutException($"Timed out: {what}.");
    }

    public ValueTask DisposeAsync() => _eventStore.DisposeAsync();

    public void Dispose() => _eventStore.Dispose();
}
