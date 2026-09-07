using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using KurrentDB.Client;
using MicroPlumberd.Migration;
using MicroPlumberd.Migration.Scripting;
using MicroPlumberd.Testing;
using Xunit;

namespace MicroPlumberd.Rewrite.Tests;

/// <summary>
/// UT-06/07 — the generic <c>Transform</c> operation inside the real copy engine, against real (in-memory)
/// KurrentDB containers. Exercised through the PUBLIC <see cref="IMigrationBuilder"/> /
/// <see cref="MigrationRunner"/> surface, exactly as the tool will use it.
/// </summary>
[Trait("Category", "Integration")]
public class CopyEngineTransformTests
{
    /// <summary>Provisions isolated source/destination stores and removes their containers afterwards.</summary>
    private sealed class Stores : IAsyncDisposable
    {
        private readonly List<EventStoreServer> _servers = [];

        public async Task<KurrentDBClient> NewStoreAsync(string tag)
        {
            // Named so a leftover container is obviously ours and can be removed by exact name — never by
            // image or ancestor filter, which on this shared docker host would hit other sessions.
            var srv = EventStoreServer.Create($"mp-rewrite-test-{tag}-{Guid.NewGuid():N}");
            _servers.Add(srv);
            await srv.StartInDocker(inMemory: true);
            return new KurrentDBClient(srv.GetEventStoreSettings());
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var s in _servers) await s.DisposeAsync();
        }
    }

    private sealed class InlineMigration(string id, Action<IMigrationBuilder> build) : MicroPlumberd.Migration.Migration
    {
        public override string Id => id;
        public override void Migrate(IMigrationBuilder b) => build(b);
    }

    private static Task<IWriteResult> AppendAsync(KurrentDBClient c, string stream, StreamState expected,
        string type, string dataJson, Uuid id) =>
        c.AppendToStreamAsync(stream, expected,
            [new EventData(id, type, Encoding.UTF8.GetBytes(dataJson), Encoding.UTF8.GetBytes("{}"))]);

    private static async Task<List<EventRecord>> ReadAsync(KurrentDBClient c, string stream)
    {
        var res = c.ReadStreamAsync(Direction.Forwards, stream, StreamPosition.Start, resolveLinkTos: false);
        if (await res.ReadState == ReadState.StreamNotFound) return [];
        var list = new List<EventRecord>();
        await foreach (var e in res) list.Add(e.Event);
        return list;
    }

    // ------------------------------------------------------------------ UT-06

    [Fact]
    public async Task UT06_Transform_retargets_the_stream_renumbers_gaplessly_preserves_event_ids_and_drops_on_null()
    {
        await using var stores = new Stores();
        var src = await stores.NewStoreAsync("ut06-src");
        var dst = await stores.NewStoreAsync("ut06-dst");

        var ids = Enumerable.Range(0, 4).Select(_ => Uuid.NewUuid()).ToArray();
        // Deliberately NON-canonical JSON (a trailing .0 and spaces): a payload nothing touched must reach the
        // destination byte-for-byte, not re-rendered.
        await AppendAsync(src, "Src-1", StreamState.NoStream, "A", """{"n": 1.0}""", ids[0]);
        await AppendAsync(src, "Src-1", 0ul, "Doomed", """{"n": 2}""", ids[1]);
        await AppendAsync(src, "Src-1", 1ul, "A", """{"n": 3}""", ids[2]);
        await AppendAsync(src, "Src-1", 2ul, "A", """{"n": 4}""", ids[3]);
        var sourceBytes = (await ReadAsync(src, "Src-1")).ToDictionary(e => e.EventId, e => e.Data.ToArray());

        var migration = new InlineMigration("0001_transform", b => b.Transform(e =>
            e.Type == "Doomed" ? null : e with { StreamId = "Dst-1" }));

        var result = await new MigrationRunner().RunAsync(src, dst, [migration], dryRun: false);

        result.Copy.Dropped.Should().Be(1);
        result.Copy.Kept.Should().Be(3);

        (await ReadAsync(dst, "Src-1")).Should().BeEmpty("every event was retargeted to Dst-1");

        var copied = await ReadAsync(dst, "Dst-1");
        copied.Select(e => e.EventNumber.ToUInt64()).Should().Equal([0ul, 1ul, 2ul],
            "the destination stream is renumbered gaplessly even though event #1 of the source was dropped");
        copied.Select(e => e.EventId).Should().Equal([ids[0], ids[2], ids[3]],
            "the source event id is the only stable handle across a rewrite and must survive it");
        copied.Select(e => e.EventType).Should().Equal(["A", "A", "A"]);
        copied[0].Data.ToArray().Should().Equal(sourceBytes[ids[0]],
            "a payload no rule touched is copied byte-for-byte, not re-serialised");

        // The history record must say WHAT the run did, not only that it ran — an operator reading a store
        // months later has nothing else to go on.
        var applied = result.NewlyApplied.Single();
        applied.Descriptors.Should().NotBeNull()
            .And.ContainSingle(d => d.StartsWith("Transform("),
                "the generic transform is the operation this migration consists of");
    }

    [Fact]
    public async Task UT06_A_Transform_that_rewrites_the_payload_is_what_lands_in_the_destination()
    {
        await using var stores = new Stores();
        var src = await stores.NewStoreAsync("ut06b-src");
        var dst = await stores.NewStoreAsync("ut06b-dst");

        var id = Uuid.NewUuid();
        await AppendAsync(src, "Order-1", StreamState.NoStream, "OrderCreated",
            """{"Customer":"old","Keep":7}""", id);

        var migration = new InlineMigration("0001_payload", b => b.Transform(e =>
        {
            var data = e.Data!.DeepClone();
            data["Customer"] = "X";
            return e with { Data = data, Type = "OrderCreatedV2" };
        }));

        await new MigrationRunner().RunAsync(src, dst, [migration], dryRun: false);

        var copied = (await ReadAsync(dst, "Order-1")).Single();
        copied.EventType.Should().Be("OrderCreatedV2");
        copied.EventId.Should().Be(id);
        var data = JsonNode.Parse(copied.Data.Span)!.AsObject();
        data["Customer"]!.GetValue<string>().Should().Be("X");
        data["Keep"]!.GetValue<int>().Should().Be(7, "a field the rule did not name must survive untouched");
    }

    // ------------------------------------------------------------------ UT-07

    /// <summary>
    /// A payload that claims to be JSON but is not cannot be handed to a script — there is no object to give
    /// it. It bypasses the script, is copied byte-for-byte, and is COUNTED so an operator sees it in the
    /// report. Remove the bypass and the script's <c>o.Data.marker</c> throws on <c>undefined</c>, so this
    /// test goes red for exactly the reason its name claims.
    /// </summary>
    [Fact]
    public async Task UT07_A_payload_that_is_not_JSON_bypasses_the_script_is_copied_verbatim_and_is_counted()
    {
        await using var stores = new Stores();
        var src = await stores.NewStoreAsync("ut07-src");
        var dst = await stores.NewStoreAsync("ut07-dst");

        var junkId = Uuid.NewUuid();
        var goodId = Uuid.NewUuid();
        var junkBytes = "this is not json at all"u8.ToArray();
        // EventData defaults the content type to application/json — this is the "declared JSON, isn't" case
        // the copy engine must never lose.
        await src.AppendToStreamAsync("Blob-1", StreamState.NoStream,
            [new EventData(junkId, "Blob", junkBytes, Encoding.UTF8.GetBytes("{}"))]);
        await AppendAsync(src, "Order-1", StreamState.NoStream, "OrderCreated", """{"n":1}""", goodId);

        var migration = new ScriptMigration("function transform(o){ o.Data.marker = 1; return o; }",
            "ut_20260907T000000");

        var result = await new MigrationRunner().RunAsync(src, dst, [migration], dryRun: false);

        result.Copy.UnparseableVerbatim.Should().Be(1, "the operator must be told this event could not be transformed");
        result.Copy.Dropped.Should().Be(0, "an unreadable payload is never data loss");

        var blob = (await ReadAsync(dst, "Blob-1")).Single();
        blob.Data.ToArray().Should().Equal(junkBytes);
        blob.EventId.Should().Be(junkId);

        // The positive anchor: the very same script DID run, and did its work, on the JSON event next to it.
        var order = (await ReadAsync(dst, "Order-1")).Single();
        JsonNode.Parse(order.Data.Span)!["marker"]!.GetValue<int>().Should().Be(1);

        // The history carries the SCRIPT's hash, not a checksum over host-delegate IL — which every script
        // shares, and which would therefore let a store rewritten by different rules look "already applied".
        result.NewlyApplied.Single().Checksum.Should().Be(migration.ScriptChecksum);
    }
}
