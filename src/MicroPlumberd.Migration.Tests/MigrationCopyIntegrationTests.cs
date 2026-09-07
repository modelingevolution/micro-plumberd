using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using KurrentDB.Client;
using MicroPlumberd.Migration;
using MicroPlumberd.Testing;
using Xunit;

namespace MicroPlumberd.Migration.Tests;

/// <summary>
/// End-to-end copy-engine tests against real KurrentDB containers (via <see cref="EventStoreServer"/>).
/// Each test provisions its own isolated SOURCE and DEST stores because the copy is whole-store.
/// </summary>
[Trait("Category", "Integration")]
public class MigrationCopyIntegrationTests
{
    private sealed class Stores : IAsyncDisposable
    {
        private readonly List<EventStoreServer> _servers = new();

        public async Task<KurrentDBClient> NewStoreAsync(string tag)
        {
            var srv = EventStoreServer.Create($"mp-mig-{tag}-{Guid.NewGuid():N}");
            _servers.Add(srv);
            await srv.StartInDocker(inMemory: true);
            return new KurrentDBClient(srv.GetEventStoreSettings());
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var s in _servers) await s.DisposeAsync();
        }
    }

    private static Task AppendAsync(KurrentDBClient c, string stream, StreamState expected,
        string type, string dataJson, string? metaJson = null)
    {
        var ev = new EventData(Uuid.NewUuid(), type, Encoding.UTF8.GetBytes(dataJson),
            Encoding.UTF8.GetBytes(metaJson ?? "{}"));
        return c.AppendToStreamAsync(stream, expected, [ev]);
    }

    private static async Task<List<ResolvedEvent>> ReadStreamAsync(KurrentDBClient c, string stream)
    {
        var res = c.ReadStreamAsync(Direction.Forwards, stream, StreamPosition.Start, resolveLinkTos: false);
        if (await res.ReadState == ReadState.StreamNotFound) return new List<ResolvedEvent>();
        var list = new List<ResolvedEvent>();
        await foreach (var e in res) list.Add(e);
        return list;
    }

    private sealed class InlineMigration(string id, Action<IMigrationBuilder> build) : Migration
    {
        public override string Id => id;
        public override void Migrate(IMigrationBuilder b) => build(b);
    }

    [Fact]
    public async Task Copy_preserves_order_and_version_stamps_MigratedAt_and_applies_rules()
    {
        await using var stores = new Stores();
        var src = await stores.NewStoreAsync("src");
        var dst = await stores.NewStoreAsync("dst");

        // Order-1: three events — the middle one carries a correlation id we expect to survive verbatim.
        await AppendAsync(src, "Order-1", StreamState.NoStream, "OrderPlaced", "{\"n\":1}");
        await AppendAsync(src, "Order-1", 0ul, "OrderPlaced",
            "{\"n\":2}", "{\"$correlationId\":\"11111111-1111-1111-1111-111111111111\"}");
        await AppendAsync(src, "Order-1", 1ul, "OldName", "{\"keep\":true}");
        // Widget-1: transformed. Junk-1: dropped whole-stream.
        await AppendAsync(src, "Widget-1", StreamState.NoStream, "Widget", "{\"a\":1}");
        await AppendAsync(src, "Junk-1", StreamState.NoStream, "Whatever", "{}");

        var migration = new InlineMigration("0001_mixed", b =>
        {
            b.DropStream("Junk-1");
            b.RenameType("OldName", "NewName");
            b.TransformJson("Widget", (data, meta) =>
            {
                data["migrated"] = true;
                meta["note"] = "widget-touched";
            });
        });

        var result = await new MigrationRunner().RunAsync(src, dst, [migration], dryRun: false);

        // Verification report is clean and drops are accounted for.
        result.Verification!.AllOk.Should().BeTrue(result.Verification.Format());
        result.Copy.Dropped.Should().Be(1);
        result.Copy.Kept.Should().Be(4);

        // Order-1 preserved: 3 events, contiguous versions 0,1,2, order intact, type renamed on the 3rd.
        var order = await ReadStreamAsync(dst, "Order-1");
        order.Should().HaveCount(3);
        order.Select(e => e.Event.EventNumber.ToUInt64()).Should().Equal(0ul, 1ul, 2ul);
        order[2].Event.EventType.Should().Be("NewName");

        // MigratedAt stamped on every copied event; original correlation id preserved verbatim.
        foreach (var e in order)
        {
            var meta = JsonNode.Parse(e.Event.Metadata.Span)!.AsObject();
            meta.ContainsKey("MigratedAt").Should().BeTrue();
        }
        var midMeta = JsonNode.Parse(order[1].Event.Metadata.Span)!.AsObject();
        midMeta["$correlationId"]!.GetValue<string>().Should().Be("11111111-1111-1111-1111-111111111111");

        // Transform applied to raw JSON (payload + metadata).
        var widget = await ReadStreamAsync(dst, "Widget-1");
        var wData = JsonNode.Parse(widget[0].Event.Data.Span)!.AsObject();
        wData["migrated"]!.GetValue<bool>().Should().BeTrue();
        var wMeta = JsonNode.Parse(widget[0].Event.Metadata.Span)!.AsObject();
        wMeta["note"]!.GetValue<string>().Should().Be("widget-touched");

        // Dropped stream absent at destination.
        (await ReadStreamAsync(dst, "Junk-1")).Should().BeEmpty();

        // History recorded.
        result.NewlyApplied.Should().ContainSingle(r => r.Id == "0001_mixed");
        result.NewlyApplied[0].Dropped.Should().Be(1);
    }

    [Fact]
    public async Task No_rule_event_is_copied_byte_verbatim_and_MigratedAt_is_still_stamped()
    {
        await using var stores = new Stores();
        var src = await stores.NewStoreAsync("vsrc");
        var dst = await stores.NewStoreAsync("vdst");

        // Deliberately NON-canonical JSON (extra whitespace) so a re-serialise would change the bytes — that
        // makes byte-equality at the destination a real proof the payload was NOT parsed/re-serialised.
        const string spacedData = "{ \"a\" : 1 ,   \"b\" : \"x\" }";
        const string meta = "{\"$correlationId\":\"11111111-1111-1111-1111-111111111111\"}";
        await AppendAsync(src, "Keep-1", StreamState.NoStream, "Kept", spacedData, meta);

        // DropStreams-only migration, targeting a DIFFERENT stream → nothing about Keep-1 is parsed.
        var migration = new InlineMigration("0001_drop_other", b => b.DropStream("Junk-1"));
        await new MigrationRunner().RunAsync(src, dst, [migration], dryRun: false);

        var copied = await ReadStreamAsync(dst, "Keep-1");
        copied.Should().HaveCount(1);
        var e = copied[0].Event;

        // DATA copied byte-for-byte — proves the common path neither parses nor re-serialises the payload.
        e.Data.ToArray().Should().Equal(Encoding.UTF8.GetBytes(spacedData));

        // METADATA still carries MigratedAt (stamped without a JsonNode round-trip) and preserves the original
        // correlation id verbatim.
        var m = JsonNode.Parse(e.Metadata.Span)!.AsObject();
        m.ContainsKey("MigratedAt").Should().BeTrue();
        m["$correlationId"]!.GetValue<string>().Should().Be("11111111-1111-1111-1111-111111111111");
    }

    [Fact]
    public async Task Tombstoned_source_stream_is_handled_without_crashing()
    {
        await using var stores = new Stores();
        var src = await stores.NewStoreAsync("tsrc");
        var dst = await stores.NewStoreAsync("tdst");

        await AppendAsync(src, "Keep-1", StreamState.NoStream, "Kept", "{\"ok\":1}");
        await AppendAsync(src, "Offer-of-dead", StreamState.NoStream, "OfferCreated", "{\"x\":1}");
        await AppendAsync(src, "Offer-of-dead", 0ul, "OfferChanged", "{\"x\":2}");
        // Hard-delete (tombstone) the offer stream — this is the real :5081 condition.
        await src.TombstoneAsync("Offer-of-dead", StreamState.Any);

        var migration = new InlineMigration("0001_drop_dead", b => b.DropStreams("Offer-of-dead"));

        // Must NOT throw despite the tombstoned stream / deleted-stream artifacts in $all.
        var result = await new MigrationRunner().RunAsync(src, dst, [migration], dryRun: false);

        (await ReadStreamAsync(dst, "Keep-1")).Should().HaveCount(1);
        (await ReadStreamAsync(dst, "Offer-of-dead")).Should().BeEmpty();
        result.Verification!.AllOk.Should().BeTrue(result.Verification.Format());
    }

    [Fact]
    public async Task DryRun_writes_nothing_but_reports_counts()
    {
        await using var stores = new Stores();
        var src = await stores.NewStoreAsync("dsrc");
        var dst = await stores.NewStoreAsync("ddst");

        await AppendAsync(src, "Keep-1", StreamState.NoStream, "Kept", "{}");
        await AppendAsync(src, "Junk-1", StreamState.NoStream, "Whatever", "{}");

        var migration = new InlineMigration("0001_drop_junk", b => b.DropStream("Junk-1"));

        var result = await new MigrationRunner().RunAsync(src, dst, [migration], dryRun: true);

        result.DryRun.Should().BeTrue();
        result.Copy.Kept.Should().Be(1);
        result.Copy.Dropped.Should().Be(1);
        result.Copy.MigrationStats["0001_drop_junk"].Dropped.Should().Be(1);
        result.Verification.Should().BeNull();
        result.NewlyApplied.Should().BeEmpty();

        // Destination truly untouched.
        (await ReadStreamAsync(dst, "Keep-1")).Should().BeEmpty();
        var all = dst.ReadAllAsync(Direction.Forwards, Position.Start);
        var userEvents = 0;
        await foreach (var e in all)
            if (e.Event is { } er && er.EventStreamId.Length > 0 && er.EventStreamId[0] != '$') userEvents++;
        userEvents.Should().Be(0);
    }

    // NOTE: the [OutputStream] merge/read-model replay proof lives in IncidentReplayIntegrationTests, which
    // rides the ACTUAL ERP path — the $et JOIN projection (TryCreateJoinProjection → fromStreams(['$et-…'])
    // .linkTo(X)) — not the $ce split projection. A $ce-based test was removed as it exercised a path the
    // app does not use.

    [Fact]
    public async Task History_travels_with_store_enabling_pending_detection_and_checksum_guard()
    {
        await using var stores = new Stores();
        var src = await stores.NewStoreAsync("hsrc");
        var d1 = await stores.NewStoreAsync("hd1");

        await AppendAsync(src, "Keep-1", StreamState.NoStream, "Kept", "{}");
        await AppendAsync(src, "Junk-1", StreamState.NoStream, "Whatever", "{}");

        var m1 = new InlineMigration("0001_drop_junk", b => b.DropStream("Junk-1"));

        // First run applies m1 → d1 gets history.
        var first = await new MigrationRunner().RunAsync(src, d1, [m1], dryRun: false);
        first.NewlyApplied.Should().ContainSingle(r => r.Id == "0001_drop_junk");

        // Re-migrating d1 with the SAME migration set → nothing pending, and history carries forward.
        var d2 = await stores.NewStoreAsync("hd2");
        var second = await new MigrationRunner().RunAsync(d1, d2, [m1], dryRun: false);
        second.PendingMigrationIds.Should().BeEmpty();
        second.NewlyApplied.Should().BeEmpty();
        var historyOnD2 = await ReadStreamAsync(d2, MigrationHistory.StreamName);
        historyOnD2.Should().ContainSingle(e => e.Event.EventType == nameof(MigrationApplied));

        // Editing an already-applied migration (same Id, different rules) is refused.
        var d3 = await stores.NewStoreAsync("hd3");
        var edited = new InlineMigration("0001_drop_junk", b => b.DropStream("Something-Else"));
        var act = async () => await new MigrationRunner().RunAsync(d1, d3, [edited], dryRun: false);
        (await act.Should().ThrowAsync<MigrationChecksumMismatchException>())
            .Which.MigrationId.Should().Be("0001_drop_junk");
    }

    // M1: a large CONTIGUOUS single-stream run must flush MID-STREAM (batch caps) yet still land every event,
    // in order, with GAPLESS versions — proving multiple appends to one stream continue the expected version.
    [Fact]
    public async Task Large_single_stream_run_flushes_mid_stream_and_preserves_gapless_order()
    {
        await using var stores = new Stores();
        var src = await stores.NewStoreAsync("bsrc");
        var dst = await stores.NewStoreAsync("bdst");

        const int n = 1200; // > MaxBatchCount (1000) → forces at least one mid-stream flush
        var expected = StreamState.NoStream;
        for (var offset = 0; offset < n; offset += 200)
        {
            var chunk = Enumerable.Range(offset, Math.Min(200, n - offset))
                .Select(i => new EventData(Uuid.NewUuid(), "Bulk",
                    Encoding.UTF8.GetBytes($"{{\"i\":{i}}}"), Encoding.UTF8.GetBytes("{}")))
                .ToArray();
            await src.AppendToStreamAsync("Bulk-1", expected, chunk);
            expected = (ulong)(offset + chunk.Length - 1);
        }

        var result = await new MigrationRunner().RunAsync(src, dst, Array.Empty<Migration>(), dryRun: false);
        result.Copy.Kept.Should().Be(n);
        result.Verification!.AllOk.Should().BeTrue(result.Verification.Format());
        // A single stream of n>MaxBatchCount events MUST have been split across multiple appends (a single big
        // append is impossible under the count cap) — gaplessness alone would be consistent with one append.
        result.Copy.DestAppendCount.Should().BeGreaterThan(1, "the count cap must split one stream into >1 append");

        var copied = await ReadStreamAsync(dst, "Bulk-1");
        copied.Should().HaveCount(n);
        // Gapless versions 0..n-1 AND payload order preserved across the mid-stream flushes.
        copied.Select(e => e.Event.EventNumber.ToUInt64()).Should().Equal(Enumerable.Range(0, n).Select(i => (ulong)i));
        copied.Select(e => JsonNode.Parse(e.Event.Data.Span)!["i"]!.GetValue<int>())
            .Should().Equal(Enumerable.Range(0, n));
    }

    // M2: an unparseable-but-declared-JSON event, in a store where a DropEvent rule forces parse-all, must be
    // copied VERBATIM (byte-for-byte), NOT dropped. Only the rule's explicit target is dropped.
    [Fact]
    public async Task Unparseable_json_event_is_copied_verbatim_not_dropped_even_with_a_DropEvent_rule()
    {
        await using var stores = new Stores();
        var src = await stores.NewStoreAsync("usrc");
        var dst = await stores.NewStoreAsync("udst");

        var invalidBytes = Encoding.UTF8.GetBytes("{ this is NOT valid json ");
        await src.AppendToStreamAsync("S-1", StreamState.NoStream,
            [new EventData(Uuid.NewUuid(), "Weird", invalidBytes, Encoding.UTF8.GetBytes("{}"))]); // json content-type
        await AppendAsync(src, "S-1", 0ul, "Good", "{\"ok\":1}");
        await AppendAsync(src, "S-1", 1ul, "Noise", "{}"); // this one IS dropped by the rule

        // A DropEvent rule → forces parse-all; it drops only type "Noise".
        var migration = new InlineMigration("0001_drop_noise", b => b.DropEvent(e => e.Type == "Noise"));
        var result = await new MigrationRunner().RunAsync(src, dst, [migration], dryRun: false);

        result.Copy.UnparseableVerbatim.Should().Be(1, "the unparseable event is counted (as verbatim, not lost)");
        result.Copy.Dropped.Should().Be(1, "only the Noise event is dropped");
        result.Verification!.AllOk.Should().BeTrue(result.Verification.Format());

        var copied = await ReadStreamAsync(dst, "S-1");
        copied.Select(e => e.Event.EventType).Should().Equal("Weird", "Good"); // Noise dropped; Weird kept
        // The unparseable payload is preserved byte-for-byte (proves verbatim copy, no corruption).
        copied[0].Event.Data.ToArray().Should().Equal(invalidBytes);
    }

    // V1 merge: two source streams RENAMED into ONE dest stream keep their interleaved $all/arrival order with
    // gapless version on the merged stream.
    [Fact]
    public async Task RenameStream_merges_two_sources_into_one_preserving_interleaved_order_and_gapless_version()
    {
        await using var stores = new Stores();
        var src = await stores.NewStoreAsync("msrc");
        var dst = await stores.NewStoreAsync("mdst");

        // Interleaved arrival across A-1 and B-1: A0, B0, A1, B1.
        await AppendAsync(src, "A-1", StreamState.NoStream, "T", "{\"v\":\"a0\"}");
        await AppendAsync(src, "B-1", StreamState.NoStream, "T", "{\"v\":\"b0\"}");
        await AppendAsync(src, "A-1", 0ul, "T", "{\"v\":\"a1\"}");
        await AppendAsync(src, "B-1", 0ul, "T", "{\"v\":\"b1\"}");

        var migration = new InlineMigration("0001_merge", b =>
        {
            b.RenameStream("A-1", "D-1");
            b.RenameStream("B-1", "D-1");
        });
        var result = await new MigrationRunner().RunAsync(src, dst, [migration], dryRun: false);
        result.Verification!.AllOk.Should().BeTrue(result.Verification.Format());

        var merged = await ReadStreamAsync(dst, "D-1");
        merged.Should().HaveCount(4);
        merged.Select(e => e.Event.EventNumber.ToUInt64()).Should().Equal(0ul, 1ul, 2ul, 3ul); // gapless
        merged.Select(e => JsonNode.Parse(e.Event.Data.Span)!["v"]!.GetValue<string>())
            .Should().Equal("a0", "b0", "a1", "b1"); // original $all arrival order
        (await ReadStreamAsync(dst, "A-1")).Should().BeEmpty();
        (await ReadStreamAsync(dst, "B-1")).Should().BeEmpty();
    }

    // M4: the write-fidelity checksum catches a dest whose CONTENT differs from the copy intent even when the
    // COUNT matches (a count-only verifier would pass it). Copy once cleanly; copy again with a transform that
    // alters the bytes; verify the altered dest against the CLEAN copy's hashes → MISMATCH on the changed
    // streams, while the clean dest verifies OK.
    [Fact]
    public async Task Verification_write_fidelity_checksum_catches_a_content_miscopy_that_counts_miss()
    {
        await using var stores = new Stores();
        var src = await stores.NewStoreAsync("csrc");
        var clean = await stores.NewStoreAsync("cclean");
        var altered = await stores.NewStoreAsync("calt");

        await AppendAsync(src, "S-1", StreamState.NoStream, "T", "{\"n\":1}");
        await AppendAsync(src, "S-1", 0ul, "T", "{\"n\":2}");
        await AppendAsync(src, "S-2", StreamState.NoStream, "U", "{\"n\":9}");

        var reserved = new HashSet<string>(StringComparer.Ordinal) { MigrationHistory.StreamName };

        // Clean copy — its DestStreamHashes capture the faithful write intent.
        var cleanResult = await new MigrationRunner().RunAsync(src, clean, Array.Empty<Migration>(), dryRun: false);

        // Positive control: the clean dest verifies OK against its own hashes.
        var okReport = await new Verifier().VerifyAsync(clean, cleanResult.Copy, reserved);
        okReport.AllOk.Should().BeTrue(okReport.Format());
        okReport.Streams.Should().OnlyContain(s => s.HashOk);

        // A second copy that ALTERS type-T bytes (adds a field) — same streams, same counts, different content.
        var alterMigration = new InlineMigration("0001_alter", b => b.TransformJson("T", (d, _) => d["x"] = 1));
        await new MigrationRunner().RunAsync(src, altered, [alterMigration], dryRun: false);

        // Verify the ALTERED dest against the CLEAN copy's hashes: counts match, but the checksum flags S-1.
        var badReport = await new Verifier().VerifyAsync(altered, cleanResult.Copy, reserved);
        badReport.AllOk.Should().BeFalse("the checksum must catch the altered content the count check misses");
        var s1 = badReport.Streams.Single(s => s.DestStream == "S-1");
        s1.ExpectedCount.Should().Be(s1.ActualCount); // count is fine …
        s1.HashOk.Should().BeFalse();                  // … but the write-fidelity checksum is not
        s1.Status.Should().Be(VerificationStatus.Mismatch);
        // Precision: S-2 (type U) was NOT altered by the T-transform, so its checksum still matches.
        badReport.Streams.Single(s => s.DestStream == "S-2").HashOk.Should().BeTrue();
    }

    // M1 byte cap: LARGE payloads must split a single stream on the BYTES branch (not the count branch), and a
    // single event LARGER than the whole cap must be appended ALONE. Uses one stream so DestAppendCount is
    // unambiguous: [1.5 MiB, 0.6 MiB, 0.6 MiB] → the oversize event flushes alone, then each 0.6 MiB event
    // exceeds the 1 MiB cap when added to a non-empty batch → 3 separate appends.
    [Fact]
    public async Task Byte_cap_splits_large_payloads_and_appends_an_oversize_event_alone()
    {
        await using var stores = new Stores();
        var src = await stores.NewStoreAsync("ysrc");
        var dst = await stores.NewStoreAsync("ydst");

        static byte[] Blob(int bytes) =>
            Encoding.UTF8.GetBytes("{\"blob\":\"" + new string('x', bytes) + "\"}");

        var big = Blob(1_200 * 1024);   // > 1 MiB cap → must be appended alone (kept well under single-event limits)
        var mid1 = Blob(600 * 1024);
        var mid2 = Blob(600 * 1024);
        await src.AppendToStreamAsync("Byte-1", StreamState.NoStream,
        [
            new EventData(Uuid.NewUuid(), "T", big, Encoding.UTF8.GetBytes("{}")),
            new EventData(Uuid.NewUuid(), "T", mid1, Encoding.UTF8.GetBytes("{}")),
            new EventData(Uuid.NewUuid(), "T", mid2, Encoding.UTF8.GetBytes("{}")),
        ]);

        var result = await new MigrationRunner().RunAsync(src, dst, Array.Empty<Migration>(), dryRun: false);
        result.Verification!.AllOk.Should().BeTrue(result.Verification.Format());
        // BYTES branch (not count: only 3 events, far below MaxBatchCount) split this one stream into 3 appends,
        // the first carrying the oversize event alone.
        result.Copy.DestAppendCount.Should().Be(3, "the byte cap must split large payloads (incl. an oversize event alone)");

        var copied = await ReadStreamAsync(dst, "Byte-1");
        copied.Should().HaveCount(3);
        copied.Select(e => e.Event.EventNumber.ToUInt64()).Should().Equal(0ul, 1ul, 2ul); // gapless across splits
        copied[0].Event.Data.ToArray().Should().Equal(big);   // oversize event copied intact, in order
        copied[1].Event.Data.ToArray().Should().Equal(mid1);
        copied[2].Event.Data.ToArray().Should().Equal(mid2);
    }

    // M4 direct reorder: two dest events with IDENTICAL bytes but SWAPPED order must fail the write-fidelity
    // checksum (the hash is order-sensitive) even though the count is unchanged — the exact property the
    // content-alteration test does not isolate.
    [Fact]
    public async Task Verification_checksum_catches_reordered_dest_events()
    {
        await using var stores = new Stores();
        var src = await stores.NewStoreAsync("rsrc");
        var clean = await stores.NewStoreAsync("rclean");
        var swapped = await stores.NewStoreAsync("rswap");

        await AppendAsync(src, "S-1", StreamState.NoStream, "T", "{\"v\":1}");
        await AppendAsync(src, "S-1", 0ul, "T", "{\"v\":2}");

        var reserved = new HashSet<string>(StringComparer.Ordinal) { MigrationHistory.StreamName };
        var cleanResult = await new MigrationRunner().RunAsync(src, clean, Array.Empty<Migration>(), dryRun: false);
        (await new Verifier().VerifyAsync(clean, cleanResult.Copy, reserved)).AllOk.Should().BeTrue();

        // Hand-build a dest with the SAME two (EventType, Data) pairs but in SWAPPED order (v2 then v1).
        await swapped.AppendToStreamAsync("S-1", StreamState.NoStream,
        [
            new EventData(Uuid.NewUuid(), "T", Encoding.UTF8.GetBytes("{\"v\":2}"), Encoding.UTF8.GetBytes("{}")),
            new EventData(Uuid.NewUuid(), "T", Encoding.UTF8.GetBytes("{\"v\":1}"), Encoding.UTF8.GetBytes("{}")),
        ]);

        var report = await new Verifier().VerifyAsync(swapped, cleanResult.Copy, reserved);
        var s1 = report.Streams.Single(s => s.DestStream == "S-1");
        s1.ExpectedCount.Should().Be(s1.ActualCount); // count is identical (2 == 2) …
        s1.HashOk.Should().BeFalse();                  // … but the order-sensitive checksum catches the swap
        report.AllOk.Should().BeFalse();
    }

    // m8a: a RenameStream/RenameType into the $-system namespace (events would vanish as system) is rejected.
    [Fact]
    public async Task Rename_into_system_namespace_is_rejected()
    {
        await using var stores = new Stores();
        var src = await stores.NewStoreAsync("gsrc");
        var dst = await stores.NewStoreAsync("gdst");
        await AppendAsync(src, "A-1", StreamState.NoStream, "T", "{}");

        var badStream = new InlineMigration("0001_bad_stream", b => b.RenameStream("A-1", "$sneaky"));
        var badType = new InlineMigration("0001_bad_type", b => b.RenameType("T", "$sneaky"));

        var s = async () => await new MigrationRunner().RunAsync(src, dst, [badStream], dryRun: true);
        await s.Should().ThrowAsync<InvalidOperationException>();

        var t = async () => await new MigrationRunner().RunAsync(src, dst, [badType], dryRun: true);
        await t.Should().ThrowAsync<InvalidOperationException>();
    }
}
