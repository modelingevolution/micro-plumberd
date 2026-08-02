using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using KurrentDB.Client;
using MicroPlumberd.Testing;
using Xunit;
using Xunit.Abstractions;

namespace MicroPlumberd.Migration.Tests;

/// <summary>
/// Exploratory SPIKES (not product code) that settle the [OutputStream] rebuild design against REAL
/// KurrentDB behavior. They answer: (1) does an app-style <c>fromStreams(['$et-A','$et-B']).linkTo(X)</c>
/// catch-up produce ORIGINAL interleaved order or TYPE-CLUSTERED order? (2) can a projection be made to
/// resume at HEAD (ignore history), and what happens if we pre-write the output stream's links?
///
/// They need Docker (KurrentDB with standard projections enabled — the EventStoreServer fixture enables
/// them). Each spike asserts a HYPOTHESIS so a failure prints the ACTUAL observed order/count, and also
/// writes observations to test output. Run: dotnet test --filter Category=Spike
///
/// HISTORICAL EVIDENCE (kept after the design was settled): Spike1 demonstrates that an app-style
/// <c>fromStreams(['$et-A','$et-B']).linkTo</c> catch-up TYPE-CLUSTERS (0,2,4,1,3) rather than preserving
/// arrival order — this is exactly why MicroPlumberd's join projection was changed to <c>fromAll()</c>
/// (commit-ordered). Spike2/Spike2c record why the pre-written-link and checkpoint-seed alternatives were
/// rejected. The recovery is proven end-to-end by the SIMPLE + fromAll flow in IncidentReplayIntegrationTests.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "Spike")]
public class ProjectionBehaviorSpikes(ITestOutputHelper output)
{
    private sealed class Store : IAsyncDisposable
    {
        public required EventStoreServer Es { get; init; }
        public required KurrentDBClient Client { get; init; }
        public required KurrentDBProjectionManagementClient Projections { get; init; }

        public static async Task<Store> StartAsync(string tag)
        {
            var es = EventStoreServer.Create($"mp-spike-{tag}-{Guid.NewGuid():N}");
            await es.StartInDocker(inMemory: true);
            var s = es.GetEventStoreSettings();
            return new Store { Es = es, Client = new KurrentDBClient(s), Projections = new KurrentDBProjectionManagementClient(s) };
        }

        public async ValueTask DisposeAsync() => await Es.DisposeAsync();
    }

    private static EventData Typed(string type, int ord) =>
        new(Uuid.NewUuid(), type, Encoding.UTF8.GetBytes($"{{\"Ord\":{ord}}}"));

    private static EventData Link(ulong eventNumber, string stream) =>
        new(Uuid.NewUuid(), "$>", Encoding.UTF8.GetBytes($"{eventNumber}@{stream}"),
            contentType: "application/octet-stream");

    // Mirrors MicroPlumberd's projection creation (create -> disable -> update emit:true -> enable).
    private async Task CreateEmittingProjectionAsync(Store s, string name, string query)
    {
        await s.Projections.CreateContinuousAsync(name, query, trackEmittedStreams: true);
        await s.Projections.DisableAsync(name);
        await s.Projections.UpdateAsync(name, query, emitEnabled: true);
        await s.Projections.EnableAsync(name);
    }

    private static string JoinQuery(string outputStream, params string[] eventTypes)
    {
        var streams = string.Join(",", eventTypes.Select(t => $"'$et-{t}'"));
        return $"fromStreams([{streams}]).when({{ $any: function(s,e){{ linkTo('{outputStream}', e) }} }});";
    }

    private static async Task<List<(string Type, int Ord)>> ReadResolvedAsync(KurrentDBClient c, string stream)
    {
        var res = c.ReadStreamAsync(Direction.Forwards, stream, StreamPosition.Start, resolveLinkTos: true);
        var list = new List<(string, int)>();
        if (await res.ReadState == ReadState.StreamNotFound) return list;
        await foreach (var re in res)
        {
            if (re.Event is null) { list.Add(("<DEAD>", -1)); continue; }
            var ord = JsonNode.Parse(re.Event.Data.Span)?["Ord"]?.GetValue<int>() ?? -1;
            list.Add((re.Event.EventType, ord));
        }
        return list;
    }

    private static async Task<long> CountAsync(KurrentDBClient c, string stream)
    {
        var res = c.ReadStreamAsync(Direction.Forwards, stream, StreamPosition.Start, resolveLinkTos: false);
        if (await res.ReadState == ReadState.StreamNotFound) return 0;
        return await res.LongCountAsync();
    }

    private static async Task WaitUntil(Func<Task<bool>> cond, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline) { if (await cond()) return; await Task.Delay(500); }
    }

    // SPIKE 1 — ordering of a from-beginning fromStreams catch-up.
    [Fact]
    public async Task Spike1_FromStreams_join_ordering_interleaved_vs_type_clustered()
    {
        await using var s = await Store.StartAsync("s1");

        // Interleaved arrival across two event types: A0, B1, A2, B3, A4.
        await s.Client.AppendToStreamAsync("Spike-1", StreamState.NoStream,
            [Typed("SpikeA", 0), Typed("SpikeB", 1), Typed("SpikeA", 2), Typed("SpikeB", 3), Typed("SpikeA", 4)]);

        // Let $by_event_type populate $et-SpikeA / $et-SpikeB.
        await WaitUntil(async () => await CountAsync(s.Client, "$et-SpikeA") >= 3
                                    && await CountAsync(s.Client, "$et-SpikeB") >= 2, TimeSpan.FromSeconds(30));

        // App-style join projection (no options()).
        await CreateEmittingProjectionAsync(s, "SpikeOut", JoinQuery("SpikeOut", "SpikeA", "SpikeB"));
        await WaitUntil(async () => await CountAsync(s.Client, "SpikeOut") >= 5, TimeSpan.FromSeconds(30));
        var appStyle = await ReadResolvedAsync(s.Client, "SpikeOut");
        output.WriteLine("app-style Ord order: " + string.Join(",", appStyle.Select(x => x.Ord)));

        // reorderEvents variant.
        var reorderQuery = "options({reorderEvents: true, processingLag: 500});\n"
                           + JoinQuery("SpikeOutR", "SpikeA", "SpikeB");
        await CreateEmittingProjectionAsync(s, "SpikeOutR", reorderQuery);
        await WaitUntil(async () => await CountAsync(s.Client, "SpikeOutR") >= 5, TimeSpan.FromSeconds(30));
        var reordered = await ReadResolvedAsync(s.Client, "SpikeOutR");
        output.WriteLine("reorderEvents Ord order: " + string.Join(",", reordered.Select(x => x.Ord)));

        // SETTLED FINDING (this is WHY MicroPlumberd's join projection was changed to fromAll): a
        // from-beginning fromStreams(['$et-A','$et-B']) catch-up over a pre-populated store TYPE-CLUSTERS — it
        // drains $et-SpikeA (0,2,4) then $et-SpikeB (1,3), yielding 0,2,4,1,3, NOT the original interleaved
        // arrival order 0,1,2,3,4. A fromAll() projection instead processes strictly in $all/commit order and
        // reproduces 0,1,2,3,4 (see IncidentReplayIntegrationTests). If a future server build ever made this
        // fromStreams catch-up preserve arrival order, it would flip to 0,1,2,3,4 and fail this loudly.
        appStyle.Select(x => x.Ord).Should().Equal(new[] { 0, 2, 4, 1, 3 },
            "fromStreams catch-up type-clusters by event type (observed {0}) — this is why the app uses fromAll",
            string.Join(",", appStyle.Select(x => x.Ord)));
    }

    // SPIKE 2 (now NON-GATING under the live-build approach; kept as evidence) — (baseline) a from-beginning
    // projection re-links ALL history; (b) pre-written links duplicate.
    [Fact]
    public async Task Spike2_from_beginning_relinks_all_history_and_prewritten_links_duplicate()
    {
        await using var s = await Store.StartAsync("s2");

        await s.Client.AppendToStreamAsync("Spike2-1", StreamState.NoStream,
            [Typed("Spike2A", 0), Typed("Spike2A", 1), Typed("Spike2A", 2)]);
        await WaitUntil(async () => await CountAsync(s.Client, "$et-Spike2A") >= 3, TimeSpan.FromSeconds(30));

        // (baseline) create the emitting projection AFTER history exists → it links ALL 3 (no from-end).
        await CreateEmittingProjectionAsync(s, "Spike2Out", JoinQuery("Spike2Out", "Spike2A"));
        await WaitUntil(async () => await CountAsync(s.Client, "Spike2Out") >= 3, TimeSpan.FromSeconds(30));
        var baseCount = await CountAsync(s.Client, "Spike2Out");
        output.WriteLine($"from-beginning projection linked {baseCount} history events (expected 3).");
        baseCount.Should().Be(3, "a from-beginning projection re-links ALL history — there is no from-end");

        // (b) pre-write the output stream's links (as a manual rebuild would), THEN create the emitting
        // projection from beginning and see whether EventStore DUPLICATES or reconciles.
        await s.Client.AppendToStreamAsync("Spike2Dup", StreamState.NoStream,
            [Link(0, "Spike2-1"), Link(1, "Spike2-1"), Link(2, "Spike2-1")]);
        await CreateEmittingProjectionAsync(s, "Spike2DupProj", JoinQuery("Spike2Dup", "Spike2A"));
        await WaitUntil(async () => await CountAsync(s.Client, "Spike2Dup") >= 4, TimeSpan.FromSeconds(15));
        await Task.Delay(2000);
        var dupCount = await CountAsync(s.Client, "Spike2Dup");
        output.WriteLine($"pre-written 3 links + projection from beginning => Spike2Dup now has {dupCount} "
                         + "(3 = reconciled/skipped; 6 = duplicated).");
        // HYPOTHESIS the rebuild would NEED: reconciliation skips the pre-written links (stays 3). If this
        // FAILS with 6, pre-writing + app-registered projection duplicates — the no-double-emit blocker.
        dupCount.Should().Be(3, "observed {0} links after regeneration over pre-written links", dupCount);
    }

    // SPIKE 2c — EXPLORATORY, best-effort: can seeding the projection checkpoint at head make it ignore
    // history? The checkpoint stream format is UNDOCUMENTED, so this is a probe: it reports whether the
    // server accepts the write and whether history is skipped. Tolerant of faults by design.
    [Fact]
    public async Task Spike2c_checkpoint_seed_at_head_exploratory()
    {
        await using var s = await Store.StartAsync("s2c");

        await s.Client.AppendToStreamAsync("Spike3-1", StreamState.NoStream,
            [Typed("Spike3A", 0), Typed("Spike3A", 1), Typed("Spike3A", 2)]);
        await WaitUntil(async () => await CountAsync(s.Client, "$et-Spike3A") >= 3, TimeSpan.FromSeconds(30));

        const string name = "Spike3Out";
        var query = JoinQuery(name, "Spike3A");

        // Create disabled, then attempt to seed a checkpoint at head before enabling.
        await s.Projections.CreateContinuousAsync(name, query, trackEmittedStreams: true);
        await s.Projections.DisableAsync(name);
        await s.Projections.UpdateAsync(name, query, emitEnabled: true);

        Position head = Position.Start;
        await foreach (var re in s.Client.ReadAllAsync(Direction.Backwards, Position.End, maxCount: 1))
        { head = re.OriginalPosition ?? head; break; }

        var accepted = false;
        try
        {
            // Best-effort guess at a checkpoint payload keyed to the $all position.
            var checkpoint = new JsonObject
            {
                ["$v"] = "1",
                ["$checkpointPosition"] = new JsonObject
                {
                    ["commitPosition"] = head.CommitPosition,
                    ["preparePosition"] = head.PreparePosition
                }
            };
            await s.Client.AppendToStreamAsync($"$projections-{name}-checkpoint", StreamState.Any,
                [new EventData(Uuid.NewUuid(), "$ProjectionCheckpoint",
                    Encoding.UTF8.GetBytes(checkpoint.ToJsonString()))]);
            accepted = true;
        }
        catch (Exception ex)
        {
            output.WriteLine($"checkpoint seed write REJECTED: {ex.GetType().Name}: {ex.Message}");
        }
        output.WriteLine($"checkpoint seed write accepted by server: {accepted}");

        await s.Projections.EnableAsync(name);

        // Append a NEW event; if the seed worked, only THIS event links (history ignored).
        await s.Client.AppendToStreamAsync("Spike3-1", StreamState.Any, [Typed("Spike3A", 99)]);
        await WaitUntil(async () => await CountAsync(s.Client, name) >= 1, TimeSpan.FromSeconds(15));
        await Task.Delay(2000);

        var linked = await ReadResolvedAsync(s.Client, name);
        output.WriteLine("linked Ord values: " + string.Join(",", linked.Select(x => x.Ord)));
        output.WriteLine($"HISTORY IGNORED (seed worked) == only [99]? {linked.Count == 1 && linked[0].Ord == 99}");
        // No hard assertion: this probe REPORTS. The design decision reads the output above.
        linked.Should().NotBeEmpty("the projection should link at least the new event once enabled");
    }
}
