using System.Security.Cryptography;
using System.Text;
using KurrentDB.Client;

namespace MicroPlumberd.Migration;

/// <summary>Whether a destination stream matched its expected count AND write-fidelity checksum.</summary>
public enum VerificationStatus
{
    /// <summary>Destination count AND checksum equal the expected (copied) values.</summary>
    Ok,

    /// <summary>Destination count or checksum differs from expected and is not an intended drop.</summary>
    Mismatch
}

/// <summary>Per destination-stream verification outcome.</summary>
/// <param name="DestStream">The destination stream.</param>
/// <param name="ExpectedCount">Events the copy engine expected to write to this stream.</param>
/// <param name="ActualCount">Events actually found in the destination stream.</param>
/// <param name="FinalVersion">The last event number (revision) in the destination stream.</param>
/// <param name="HashOk">Whether the dest stream's recomputed write-fidelity checksum matched the copy's.</param>
/// <param name="Status">Whether the counts AND checksum matched.</param>
public sealed record StreamVerification(
    string DestStream,
    long ExpectedCount,
    long ActualCount,
    ulong FinalVersion,
    bool HashOk,
    VerificationStatus Status);

/// <summary>A source stream that produced no destination events (an intended full drop).</summary>
/// <param name="SourceStream">The fully-dropped source stream.</param>
/// <param name="SourceCount">How many events were dropped from it.</param>
public sealed record DroppedStreamInfo(string SourceStream, long SourceCount);

/// <summary>
/// Cross-checks the destination store against what the copy engine believes it wrote — both per-stream event
/// COUNTS and a per-stream write-fidelity CHECKSUM (recomputed from the dest). It verifies the copy INTENT
/// reached the dest faithfully (no dest-side reorder/truncation/corruption); it does NOT re-derive events from
/// the source or re-check transform semantics (those are covered by tests). Mismatches not explained by an
/// intended drop are flagged.
/// </summary>
public sealed class VerificationReport
{
    /// <summary>Per destination-stream verification rows.</summary>
    public required IReadOnlyList<StreamVerification> Streams { get; init; }

    /// <summary>Source streams that were fully dropped (intended).</summary>
    public required IReadOnlyList<DroppedStreamInfo> DroppedStreams { get; init; }

    /// <summary>True when every destination stream matches its expected count.</summary>
    public bool AllOk => Streams.All(s => s.Status == VerificationStatus.Ok);

    /// <summary>Renders a human-readable report (per-stream expected vs actual counts and final versions).</summary>
    public string Format()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Verification report (source vs destination):");
        sb.AppendLine($"  Destination streams: {Streams.Count}");
        foreach (var s in Streams.OrderBy(s => s.DestStream, StringComparer.Ordinal))
        {
            var flag = s.Status == VerificationStatus.Ok ? "OK " : "!! ";
            var hash = s.HashOk ? "" : " [CHECKSUM MISMATCH]";
            sb.AppendLine(
                $"  {flag}{s.DestStream}: expected {s.ExpectedCount}, actual {s.ActualCount}, final v{s.FinalVersion}{hash}");
        }
        if (DroppedStreams.Count > 0)
        {
            sb.AppendLine($"  Fully-dropped source streams (intended): {DroppedStreams.Count}");
            foreach (var d in DroppedStreams.OrderBy(d => d.SourceStream, StringComparer.Ordinal))
                sb.AppendLine($"     - {d.SourceStream} ({d.SourceCount} event(s) dropped)");
        }
        sb.AppendLine(AllOk ? "  RESULT: OK" : "  RESULT: MISMATCH — see !! lines above.");
        return sb.ToString();
    }
}

internal sealed class Verifier
{
    public async Task<VerificationReport> VerifyAsync(KurrentDBClient dest, CopyResult copy,
        IReadOnlySet<string> reservedStreams, CancellationToken ct = default)
    {
        // Expected destination counts = kept events grouped by their target stream.
        var expected = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var info in copy.SourceStreams.Values)
        {
            if (info.Kept == 0) continue;
            expected.TryGetValue(info.TargetStream, out var cur);
            expected[info.TargetStream] = cur + info.Kept;
        }

        // Actual destination counts + final revision, RE-READ independently from the destination log, plus a
        // per-stream rolling write-fidelity hash recomputed over each event's (EventType || 0x00 || Data) in
        // dest write order (M4). Reading $all forward yields each stream's events in event-number = write order,
        // so the recomputed hash catches dest-side REORDER / TRUNCATION / CORRUPTION that a count check cannot.
        // $>/`$`-typed events are skipped SYMMETRICALLY with the copy engine (it never wrote them).
        var actual = new Dictionary<string, long>(StringComparer.Ordinal);
        var lastRev = new Dictionary<string, ulong>(StringComparer.Ordinal);
        var hashers = new Dictionary<string, IncrementalHash>(StringComparer.Ordinal);
        var read = dest.ReadAllAsync(Direction.Forwards, Position.Start, resolveLinkTos: false,
            cancellationToken: ct);
        await foreach (var re in read.ConfigureAwait(false))
        {
            var er = re.Event;
            if (er is null) continue;
            if (er.EventStreamId.Length > 0 && er.EventStreamId[0] == '$') continue;
            if (reservedStreams.Contains(er.EventStreamId)) continue;
            if (er.EventType.Length > 0 && er.EventType[0] == '$') continue;
            actual.TryGetValue(er.EventStreamId, out var c);
            actual[er.EventStreamId] = c + 1;
            lastRev[er.EventStreamId] = er.EventNumber.ToUInt64();
            FeedHash(hashers, er.EventStreamId, er.EventType, er.Data.Span);
        }

        var actualHash = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (stream, h) in hashers)
        {
            actualHash[stream] = Convert.ToHexString(h.GetHashAndReset());
            h.Dispose();
        }

        var streams = new List<StreamVerification>();
        foreach (var name in expected.Keys.Union(actual.Keys))
        {
            expected.TryGetValue(name, out var exp);
            actual.TryGetValue(name, out var act);
            lastRev.TryGetValue(name, out var fv);
            // HashOk when the copy recorded an expected hash for this stream and the dest recomputation matches.
            // Streams with no expected hash (none written, e.g. a fully-dropped stream) are vacuously hash-ok.
            var hashOk = !copy.DestStreamHashes.TryGetValue(name, out var expHash)
                         || (actualHash.TryGetValue(name, out var actHash) && actHash == expHash);
            var status = exp == act && hashOk ? VerificationStatus.Ok : VerificationStatus.Mismatch;
            streams.Add(new StreamVerification(name, exp, act, fv, hashOk, status));
        }

        var dropped = copy.SourceStreams
            .Where(kv => kv.Value.SourceCount > 0 && kv.Value.Kept == 0)
            .Select(kv => new DroppedStreamInfo(kv.Key, kv.Value.Dropped))
            .ToList();

        return new VerificationReport { Streams = streams, DroppedStreams = dropped };
    }

    private static readonly byte[] HashSeparator = [0x00];

    private static void FeedHash(Dictionary<string, IncrementalHash> hashers, string stream, string eventType,
        ReadOnlySpan<byte> data)
    {
        if (!hashers.TryGetValue(stream, out var h))
            hashers[stream] = h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var max = Encoding.UTF8.GetMaxByteCount(eventType.Length);
        byte[]? rented = max > 512 ? System.Buffers.ArrayPool<byte>.Shared.Rent(max) : null;
        Span<byte> buf = rented ?? stackalloc byte[512];
        var n = Encoding.UTF8.GetBytes(eventType, buf);
        h.AppendData(buf[..n]);
        if (rented is not null) System.Buffers.ArrayPool<byte>.Shared.Return(rented);
        h.AppendData(HashSeparator);
        h.AppendData(data);
    }
}
