using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KurrentDB.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MicroPlumberd.Migration;

/// <summary>Per source-stream copy accounting.</summary>
public sealed class StreamCopyInfo
{
    /// <summary>The destination stream these events were written to (after any rename).</summary>
    public required string TargetStream { get; set; }

    /// <summary>Events read from this source stream (excludes system/unparseable events).</summary>
    public long SourceCount { get; set; }

    /// <summary>Events from this source stream written to the destination.</summary>
    public long Kept { get; set; }

    /// <summary>Events from this source stream dropped by migration rules.</summary>
    public long Dropped { get; set; }
}

/// <summary>The outcome of a copy (or dry) run.</summary>
public sealed class CopyResult
{
    /// <summary>True when the run wrote nothing to the destination.</summary>
    public required bool DryRun { get; init; }

    /// <summary>The UTC instant stamped as <c>MigratedAt</c> on every copied event in this run.</summary>
    public required DateTime RunTimeUtc { get; init; }

    /// <summary>Total user events scanned from the source (system/`$` streams excluded).</summary>
    public long SourceEvents { get; internal set; }

    /// <summary>Total events written to the destination (0 on a dry run).</summary>
    public long Kept { get; internal set; }

    /// <summary>Total events dropped by migration rules.</summary>
    public long Dropped { get; internal set; }

    /// <summary>
    /// Events whose payload was declared JSON but could not be parsed and were therefore COPIED VERBATIM
    /// (byte-for-byte) rather than dropped — a warning count, NOT data loss. Their bytes are preserved exactly.
    /// </summary>
    public long UnparseableVerbatim { get; internal set; }

    /// <summary>
    /// Link events (<c>$&gt;</c>) skipped in non-system streams — i.e. <c>[OutputStream]</c> merge/link
    /// streams. These are projection output (built by continuous <c>linkTo</c> projections the app
    /// re-registers on the destination), so the destination regenerates them from the migrated aggregate
    /// streams; copying them would duplicate links once the projection re-runs.
    /// </summary>
    public long LinkEventsSkipped { get; internal set; }

    /// <summary>Per-migration effect counters.</summary>
    public required IReadOnlyDictionary<string, MigrationStats> MigrationStats { get; init; }

    /// <summary>Per source-stream accounting, keyed by source stream id.</summary>
    public Dictionary<string, StreamCopyInfo> SourceStreams { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Final revision (last event number) per destination stream. Computed on a dry run too (the append is
    /// skipped, but the revision bookkeeping still runs so counts/verification can be reported).
    /// </summary>
    public Dictionary<string, ulong> DestFinalRevision { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Number of batched append operations performed against the destination (each a single
    /// <c>AppendToStreamAsync</c> of a non-empty batch). More than one per stream means the batch caps forced a
    /// SPLIT — a large/bulk stream was written across several appends. Counted on a dry run too (it reflects the
    /// appends that WOULD be issued).
    /// </summary>
    public long DestAppendCount { get; internal set; }

    /// <summary>
    /// Write-fidelity checksum per destination stream: the hex SHA-256 of every written event's
    /// <c>(EventType UTF-8 || 0x00 || Data bytes)</c>, folded in write order. The verifier re-reads each dest
    /// stream and recomputes the same hash to catch dest-side REORDER / TRUNCATION / CORRUPTION that a count
    /// check cannot. Empty on a dry run (nothing was written). This checks the copy INTENT reached the dest
    /// faithfully — NOT source-re-derivation or transform semantics (those are covered by tests).
    /// </summary>
    public Dictionary<string, string> DestStreamHashes { get; } = new(StringComparer.Ordinal);
}

/// <summary>
/// The offline copy engine: reads a SOURCE store in <c>$all</c> commit order, applies the pending
/// migrations' raw rules to each event, and writes the surviving events to a fresh DESTINATION store,
/// per stream, preserving per-stream order and (gapless) version.
/// </summary>
/// <remarks>
/// <para><b>Skipping.</b> Every <c>$</c>-prefixed stream is skipped ($ce-*, $et-*, $streams, $scavenges,
/// projection/stats streams, stream-metadata `$$…`) — the destination regenerates its own system
/// projections — as is any explicitly reserved stream (the migration-history stream). Events whose
/// <em>type</em> starts with <c>$</c> (e.g.
/// <c>$streamDeleted</c>, <c>$metadata</c>, link events) are skipped too, which is how tombstone /
/// StreamDeleted markers are handled without crashing. Events whose payload is declared JSON but cannot be
/// parsed are NEVER dropped — they are copied VERBATIM (byte-for-byte) and counted as
/// <see cref="CopyResult.UnparseableVerbatim"/>.</para>
/// <para><b>Tombstoned source streams.</b> Reading <c>$all</c> with <c>resolveLinkTos:false</c> never
/// dereferences the null link-targets a tombstoned stream leaves in its <c>$ce</c> category stream, so a
/// source that already contains hard-tombstoned streams is copied without error.</para>
/// <para><b>Streaming.</b> The source log is streamed, never materialised. Writes are batched only across
/// events that are adjacent in <c>$all</c> and target the same destination stream (i.e. same-commit writes),
/// which keeps order and version exact.</para>
/// </remarks>
internal sealed class CopyEngine(ILogger? logger = null) : IMigrationOpLog
{
    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    /// <summary>KurrentDB's link-event type, emitted by <c>linkTo</c> projections into merge streams.</summary>
    private const string LinkEventType = "$>";

    /// <summary>
    /// Max events per single <c>AppendToStreamAsync</c>. Batches are capped so one bulk/dominant stream (or many
    /// streams renamed into one) never builds an oversized append that exceeds KurrentDB's per-append limits.
    /// </summary>
    private const int MaxBatchCount = 1000;

    /// <summary>
    /// Max cumulative payload bytes per append — a conservative fraction of KurrentDB's MaxAppendSize (~16 MiB).
    /// The batch flushes BEFORE adding an event that would exceed this (or <see cref="MaxBatchCount"/>). A single
    /// event larger than this is appended alone.
    /// </summary>
    private const int MaxBatchBytes = 1 * 1024 * 1024;

    /// <summary>Rough per-event framing overhead (id/type/headers) added to payload size for the byte cap.</summary>
    private const int PerEventFramingOverheadBytes = 128;

    private static readonly byte[] HashSeparator = [0x00];

    public async Task<CopyResult> RunAsync(
        KurrentDBClient source,
        KurrentDBClient dest,
        MigrationPlan plan,
        DateTime runTimeUtc,
        bool dryRun,
        IReadOnlySet<string> reservedStreams,
        MigrationProgressTracker? progress = null,
        IEventPacer? pacer = null,
        CancellationToken ct = default)
    {
        var result = new CopyResult
        {
            DryRun = dryRun,
            RunTimeUtc = runTimeUtc,
            MigrationStats = plan.Stats
        };

        // Pre-flight: a real run REQUIRES a fresh, empty destination (every first append expects NoStream).
        // Fail up front with a clear message instead of a raw WrongExpectedVersion mid-copy.
        if (!dryRun)
            await EnsureDestinationEmptyAsync(dest, ct).ConfigureAwait(false);

        // Capture the LAST COPYABLE event's $all commit position up front so event-copy progress is a real
        // percentage that reaches 100% (position-based; counting every event would need a second full $all
        // scan). It must exclude the trailing $-system / $> merge-link events the copy skips — otherwise the
        // copy's last processed position sits before the raw $all head and the percent never reaches 100.
        if (progress is not null)
            progress.EventCopyPlan(await ReadLastCopyablePositionAsync(source, reservedStreams, ct)
                .ConfigureAwait(false));

        // Reusable buffer + JSON writer. The metadata MigratedAt re-stamp runs for EVERY copied event (it is
        // inherent to that feature — one JsonDocument parse + one output byte[] per event); the event payload is
        // additionally re-serialised only for the transformed minority. Reusing one writer (Reset per use)
        // avoids a per-event intermediate string (ToJsonString) and buffer re-allocation. The copy loop is
        // single-threaded, so a single writer + a single reused EventContext are safe.
        var scratch = new ArrayBufferWriter<byte>(1024);
        using var writer = new Utf8JsonWriter(scratch);
        var ctx = new EventContext { SourceStream = "", EventNumber = 0, TargetStream = "", Type = "" };

        // Per-dest-stream rolling SHA-256 over each written event (write-fidelity checksum for the verifier).
        var hashers = new Dictionary<string, IncrementalHash>(StringComparer.Ordinal);

        // Pending batch of adjacent same-target-stream events, capped by count AND cumulative bytes (M1).
        string? batchStream = null;
        var batch = new List<EventData>();
        var batchBytes = 0L;

        async Task FlushAsync()
        {
            if (batchStream is null || batch.Count == 0) return;

            StreamState expected;
            ulong newLast;
            if (result.DestFinalRevision.TryGetValue(batchStream, out var last))
            {
                expected = last;                       // implicit ulong -> StreamRevision
                newLast = last + (ulong)batch.Count;
            }
            else
            {
                expected = StreamState.NoStream;
                newLast = (ulong)(batch.Count - 1);
            }

            if (!dryRun)
                await dest.AppendToStreamAsync(batchStream, expected, batch, cancellationToken: ct)
                    .ConfigureAwait(false);

            result.DestAppendCount++;                         // one append op (a split shows up as >1 per stream)
            result.DestFinalRevision[batchStream] = newLast; // supports multiple flushes to the SAME stream (gapless)
            batch.Clear();
            batchBytes = 0;
            batchStream = null;
        }

        // Folds a written event into its dest-stream rolling hash: SHA256(EventType || 0x00 || Data), in order.
        void FeedHash(string destStream, EventData ed)
        {
            if (!hashers.TryGetValue(destStream, out var h))
                hashers[destStream] = h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var type = ed.Type;
            var max = Encoding.UTF8.GetMaxByteCount(type.Length);
            byte[]? rented = max > 512 ? ArrayPool<byte>.Shared.Rent(max) : null;
            Span<byte> buf = rented ?? stackalloc byte[512];
            var n = Encoding.UTF8.GetBytes(type, buf);
            h.AppendData(buf[..n]);
            if (rented is not null) ArrayPool<byte>.Shared.Return(rented);
            h.AppendData(HashSeparator);
            h.AppendData(ed.Data.Span);
        }

        var read = source.ReadAllAsync(Direction.Forwards, Position.Start, resolveLinkTos: false,
            cancellationToken: ct);

        await foreach (var re in read.ConfigureAwait(false))
        {
            // Distinguish the three stream classes:
            //  1. system streams ($ce-*, $et-*, $streams, $$meta, projections, stats, $scavenges) — SKIP;
            //     KurrentDB regenerates them on the destination.
            //  2. [OutputStream] merge/link streams — their events are $> links. SKIP + count; they are
            //     projection output and the destination's re-registered linkTo projections rebuild them
            //     from the migrated aggregate streams (copying would duplicate links).
            //  3. regular aggregate/domain streams — copied raw through the rule engine (below).
            var er = re.Event;
            if (er is null) continue;                                  // unresolved artifact
            if (er.EventStreamId.Length > 0 && er.EventStreamId[0] == '$') continue; // (1) system stream
            if (reservedStreams.Contains(er.EventStreamId)) continue;  // reserved (history)
            if (er.EventType == LinkEventType)                         // (2) merge/link-stream link
            {
                result.LinkEventsSkipped++;
                continue;
            }
            if (er.EventType.Length > 0 && er.EventType[0] == '$') continue; // other system/tombstone event

            var sourceStream = er.EventStreamId;

            // PLAN-DRIVEN PARSE: parse the payload ONLY when a rule needs it — a TransformJson targets this
            // type, OR any DropEvent rule exists (its predicate is handed PARSED JSON). The common case (e.g.
            // _0001 is DropStreams-only) parses NOTHING here and copies the event byte-verbatim below.
            var parse = plan.RequiresPayloadParse(er.EventType);
            JsonNode? data = null;
            JsonNode? meta = null;
            if (parse)
            {
                data = TryParseJson(er.Data);
                if (data is null && !er.Data.IsEmpty && IsJsonContent(er.ContentType))
                {
                    // Declared JSON but unparseable. Do NOT drop it (M2) — copy it VERBATIM. Data stays null so
                    // BuildEventData writes the ORIGINAL bytes unchanged; a DropEvent predicate still sees null
                    // and may choose to drop; TransformJson skips a null payload. Never lose an event to a parse
                    // failure the migration didn't ask to act on.
                    _logger.LogWarning("Unparseable JSON payload for {Type} in {Stream}#{Num} — copied VERBATIM "
                        + "(not dropped).", er.EventType, sourceStream, er.EventNumber.ToUInt64());
                    result.UnparseableVerbatim++;
                }
                meta = TryParseJson(er.Metadata); // metadata may be valid even when the payload isn't
            }

            var info = GetOrAdd(result, sourceStream);
            info.SourceCount++;
            result.SourceEvents++;

            // Reuse ONE EventContext across the loop (m5) — it never escapes except into the synchronous
            // DropEvent predicate, which takes an immutable AsRawEvent() snapshot.
            ctx.SourceStream = sourceStream;
            ctx.EventNumber = er.EventNumber.ToUInt64();
            ctx.TargetStream = sourceStream;
            ctx.Type = er.EventType;
            ctx.Data = data;
            ctx.Meta = meta;
            ctx.Dropped = false;
            ctx.Transformed = false;

            plan.Apply(ctx, this);

            if (ctx.Dropped)
            {
                info.Dropped++;
                result.Dropped++;
                // Advance progress by position even for dropped events, so the percentage still reaches 100
                // if the last copyable event happens to be dropped by a rule.
                progress?.EventCopied(result.Kept, sourceStream, er.Position.CommitPosition);
                continue;
            }

            // Retarget accounting to the (possibly renamed) destination stream.
            info.TargetStream = ctx.TargetStream;
            info.Kept++;
            result.Kept++;

            var ed = BuildEventData(er, ctx, runTimeUtc, scratch, writer);
            var edSize = ed.Data.Length + ed.Metadata.Length + PerEventFramingOverheadBytes;

            // Flush BEFORE adding when the target stream changes OR the batch would exceed either cap (M1). A
            // same-stream cap-flush is safe: DestFinalRevision continues the expected version → gapless.
            if (batchStream is not null &&
                (batchStream != ctx.TargetStream || batch.Count >= MaxBatchCount || batchBytes + edSize > MaxBatchBytes))
                await FlushAsync().ConfigureAwait(false);
            batchStream ??= ctx.TargetStream;
            batch.Add(ed);
            batchBytes += edSize;
            if (!dryRun) FeedHash(ctx.TargetStream, ed); // write-fidelity checksum (real run only)

            // PACED mode (projection copy): flush THIS event immediately and wait for the join projection(s) to
            // emit its link before the next write — so the projection is kept caught up event-by-event and can
            // never accumulate a backlog (the only thing a fromStreams catch-up type-clusters). No-op on dry run.
            if (pacer is not null && !dryRun)
            {
                await FlushAsync().ConfigureAwait(false);
                await pacer.AfterAppendedAsync(ctx.Type, ct).ConfigureAwait(false);
            }

            // Report progress (throttled inside the tracker): count written + current stream + $all position.
            progress?.EventCopied(result.Kept, sourceStream, er.Position.CommitPosition);
        }

        await FlushAsync().ConfigureAwait(false);

        // Finalise the per-dest-stream write-fidelity hashes for the verifier.
        foreach (var (stream, h) in hashers)
        {
            result.DestStreamHashes[stream] = Convert.ToHexString(h.GetHashAndReset());
            h.Dispose();
        }

        _logger.LogInformation(
            "{Mode}: scanned {Source} user event(s); kept {Kept}, dropped {Dropped}, unparseable-verbatim {Verbatim}, "
            + "merge-link events skipped {Links} (regenerated by dest projections).",
            dryRun ? "Dry run" : "Copy", result.SourceEvents, result.Kept, result.Dropped, result.UnparseableVerbatim,
            result.LinkEventsSkipped);
        return result;
    }

    void IMigrationOpLog.TransformSkippedNonJson(string type, string stream, ulong eventNumber) =>
        _logger.LogWarning("TransformJson('{Type}') skipped for non-JSON payload in {Stream}#{Num}.",
            type, stream, eventNumber);

    // The $all commit position of the last COPYABLE event — the last event the copy will actually append, so
    // the copy reaches 100% when it processes it. Scans $all backwards, skipping the same classes the copy
    // skips ($-streams, reserved streams, $>/$-typed events), and returns the first match (0 if none).
    private static async Task<ulong> ReadLastCopyablePositionAsync(KurrentDBClient source,
        IReadOnlySet<string> reservedStreams, CancellationToken ct)
    {
        await foreach (var re in source
                           .ReadAllAsync(Direction.Backwards, Position.End, resolveLinkTos: false,
                               cancellationToken: ct).ConfigureAwait(false))
        {
            var er = re.Event;
            if (er is null) continue;
            if (er.EventStreamId.Length > 0 && er.EventStreamId[0] == '$') continue;
            if (reservedStreams.Contains(er.EventStreamId)) continue;
            if (er.EventType == LinkEventType) continue;
            if (er.EventType.Length > 0 && er.EventType[0] == '$') continue;
            return er.Position.CommitPosition;
        }
        return 0;
    }

    private static StreamCopyInfo GetOrAdd(CopyResult result, string sourceStream)
    {
        if (!result.SourceStreams.TryGetValue(sourceStream, out var info))
            result.SourceStreams[sourceStream] = info = new StreamCopyInfo { TargetStream = sourceStream };
        return info;
    }

    // Fails fast unless the destination has no user (non-$) events. A truly fresh node still has system
    // streams ($stats/$scavenges/standard-projection state), so only non-$ streams signal "not empty".
    private static async Task EnsureDestinationEmptyAsync(KurrentDBClient dest, CancellationToken ct)
    {
        await foreach (var re in dest.ReadAllAsync(Direction.Forwards, Position.Start, cancellationToken: ct)
                           .ConfigureAwait(false))
        {
            var er = re.Event;
            if (er is null) continue;
            if (er.EventStreamId.Length > 0 && er.EventStreamId[0] == '$') continue;
            throw new InvalidOperationException(
                $"Destination store is not empty: found stream '{er.EventStreamId}'. The migration requires "
                + "a FRESH, empty destination (start a new EventStore on a clean volume).");
        }
    }

    private static EventData BuildEventData(EventRecord er, EventContext ctx, DateTime runTimeUtc,
        ArrayBufferWriter<byte> scratch, Utf8JsonWriter writer)
    {
        // DATA: re-serialise ONLY when a TransformJson actually mutated the payload; otherwise copy the
        // original bytes VERBATIM (no parse, no string, no copy). A stream/type rename never touches the
        // payload, so those events still copy verbatim.
        ReadOnlyMemory<byte> dataBytes = ctx is { Transformed: true, Data: not null }
            ? SerializeNode(ctx.Data, scratch, writer)
            : er.Data;

        // METADATA: stamp MigratedAt. When a transform mutated the metadata, re-serialise the mutated node —
        // stamping MigratedAt when it is an object, or writing the mutated non-object node as-is (m6: never
        // silently discard a transform's metadata mutation by falling back to the original bytes). Otherwise
        // stamp the ORIGINAL metadata bytes — object → copy props + append MigratedAt via the pooled writer (no
        // intermediate string); non-object/non-JSON → verbatim; empty → a fresh object.
        ReadOnlyMemory<byte> metaBytes = ctx switch
        {
            { Transformed: true, Meta: JsonObject metaObj } => SerializeNode(StampNode(metaObj, runTimeUtc), scratch, writer),
            { Transformed: true, Meta: { } mutated } => SerializeNode(mutated, scratch, writer),
            _ => StampMetadataBytes(er.Metadata, runTimeUtc, scratch, writer)
        };

        // Preserve the ORIGINAL event id (M3) — each event is written once to a fresh dest, and keeping the id
        // holds any causation/correlation-by-id references and the idempotency key intact. The type may have
        // been renamed; the source content type is preserved.
        return new EventData(er.EventId, ctx.Type, dataBytes, metaBytes, er.ContentType);
    }

    // Serialise a JsonNode straight to UTF-8 bytes via the pooled writer — no intermediate string. The result
    // is copied out (ToArray) because the batch outlives the reused buffer.
    private static byte[] SerializeNode(JsonNode node, ArrayBufferWriter<byte> scratch, Utf8JsonWriter writer)
    {
        scratch.ResetWrittenCount();
        writer.Reset();
        node.WriteTo(writer);
        writer.Flush();
        return scratch.WrittenSpan.ToArray();
    }

    private static JsonObject StampNode(JsonObject metaObj, DateTime runTimeUtc)
    {
        metaObj["MigratedAt"] = JsonValue.Create(runTimeUtc);
        return metaObj;
    }

    // Stamp MigratedAt onto the ORIGINAL metadata bytes without an intermediate string. Empty → fresh object;
    // JSON object → copy props (overwriting any existing MigratedAt) + append via the pooled writer;
    // non-object JSON or non-JSON → verbatim (a scalar/array cannot carry a property).
    private static ReadOnlyMemory<byte> StampMetadataBytes(ReadOnlyMemory<byte> metaBytes, DateTime runTimeUtc,
        ArrayBufferWriter<byte> scratch, Utf8JsonWriter writer)
    {
        if (metaBytes.IsEmpty)
            return SerializeNode(new JsonObject { ["MigratedAt"] = JsonValue.Create(runTimeUtc) }, scratch, writer);

        JsonDocument doc;
        try { doc = JsonDocument.Parse(metaBytes); }
        catch (JsonException) { return metaBytes; } // non-JSON metadata — verbatim

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return metaBytes; // array/scalar metadata — cannot add a property; verbatim

            scratch.ResetWrittenCount();
            writer.Reset();
            writer.WriteStartObject();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.NameEquals("MigratedAt")) continue; // overwrite any pre-existing stamp
                prop.WriteTo(writer);
            }
            writer.WriteString("MigratedAt", runTimeUtc);
            writer.WriteEndObject();
            writer.Flush();
            return scratch.WrittenSpan.ToArray();
        }
    }

    private static bool IsJsonContent(string? contentType) =>
        contentType is null || contentType.Contains("json", StringComparison.OrdinalIgnoreCase);

    private static JsonNode? TryParseJson(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.IsEmpty) return null;
        try
        {
            var reader = new Utf8JsonReader(bytes.Span);
            return JsonNode.Parse(ref reader);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
