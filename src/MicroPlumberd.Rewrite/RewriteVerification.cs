using KurrentDB.Client;
using MicroPlumberd.Migration;

namespace MicroPlumberd.Rewrite;

/// <summary>What a compiled rule set can explain about a whole stream disappearing.</summary>
/// <param name="StreamDrops">Predicates that drop a stream outright, asked by name.</param>
/// <param name="StreamRenames">Declared stream renames, so a drop rule can be asked about the renamed name too.</param>
/// <param name="HasEventLevelDrop">
/// True when a <c>DropEvent</c> or a generic <c>Transform</c> is registered. Either can empty any stream one
/// event at a time, and neither can be asked about a stream without replaying the copy — so their presence
/// makes every disappearance attributable, and the gate below is only decisive without them.
/// </param>
public sealed record RuleReach(
    IReadOnlyList<Func<string, bool>> StreamDrops,
    IReadOnlyList<(string From, string To)> StreamRenames,
    bool HasEventLevelDrop)
{
    /// <summary>A rule set that explains nothing — what a pure copy has.</summary>
    public static RuleReach None { get; } = new([], [], false);

    /// <summary>Whether some declared rule accounts for <paramref name="stream"/> vanishing entirely.</summary>
    public bool ExplainsLossOf(string stream)
    {
        if (HasEventLevelDrop) return true;
        if (StreamDrops.Any(p => p(stream))) return true;
        // A DropStream declared AFTER a RenameStream sees the new name, so ask about that too.
        foreach (var (from, to) in StreamRenames)
            if (string.Equals(from, stream, StringComparison.Ordinal) && StreamDrops.Any(p => p(to)))
                return true;
        return false;
    }
}

/// <summary>One thing the gate could not account for. Any of these aborts the run before the swap.</summary>
/// <param name="Subject">The stream it is about.</param>
/// <param name="Reason">What does not add up, in the operator's terms.</param>
public sealed record VerificationIssue(string Subject, string Reason);

/// <summary>
/// The check `requirements.md` actually asks for: **did anything vanish that no rule dropped?**
/// </summary>
/// <remarks>
/// <para>The library's <see cref="VerificationReport"/> is worth having and is kept — it re-reads the
/// destination and catches truncation, reordering and corruption through a per-stream write hash. What it
/// cannot do is answer this question, because both sides of its comparison come from the copy engine's own
/// bookkeeping: an event the engine never counted lowers its expectation and its result together, and the
/// destination then matches exactly. A stream emptied by a bug is even reported as an *intended* drop.</para>
/// <para>So this gate brings its own facts. <see cref="RecountSourceAsync"/> reads the SOURCE again and counts
/// per stream; <see cref="Check"/> compares that against what the engine believed it read, insists every event
/// was either kept or dropped, and requires a rule to account for any stream that vanished.</para>
/// </remarks>
public static class RewriteVerification
{
    /// <summary>
    /// The <c>$all</c> commit position of the last event the copy would COPY — the source's head, ignoring the
    /// system and link traffic that keeps moving on an idle store.
    /// </summary>
    /// <remarks>
    /// This is the run's anchor. The guards can only say "nobody was writing" at the instant they ran, and
    /// between that instant and the swap sit an unbounded confirmation prompt and the whole copy, with the old
    /// store up and writable. Re-reading this immediately before the swap is what turns "nobody was writing
    /// then" into "nobody wrote at all" — and an event appended after the copy's read passed its position would
    /// otherwise be destroyed by the swap, silently, because the engine never saw it and so its own
    /// bookkeeping balances perfectly without it.
    /// <para>System streams and link events are skipped for a reason: an idle KurrentDB writes <c>$stats</c>
    /// and its projections emit links continuously, so an anchor over raw <c>$all</c> would advance on its own
    /// and refuse every run.</para>
    /// </remarks>
    public static async Task<ulong> ReadSourceHeadAsync(KurrentDBClient source,
        IReadOnlySet<string> reservedStreams, CancellationToken ct = default)
    {
        await foreach (var re in source
                           .ReadAllAsync(Direction.Backwards, Position.End, resolveLinkTos: false,
                               cancellationToken: ct).ConfigureAwait(false))
        {
            var er = re.Event;
            if (er is null) continue;
            if (er.EventStreamId.Length > 0 && er.EventStreamId[0] == '$') continue;
            if (reservedStreams.Contains(er.EventStreamId)) continue;
            if (er.EventType.Length > 0 && er.EventType[0] == '$') continue;
            return er.Position.CommitPosition;
        }
        return 0;
    }

    /// <summary>
    /// Counts the source's copyable events per stream, independently of the copy engine.
    /// </summary>
    /// <remarks>
    /// The classification below deliberately RE-STATES the engine's rather than sharing it. Sharing would make
    /// the recount inherit the very bug it exists to catch — a classifier that wrongly discards a stream would
    /// discard it identically on both sides and the check would agree with itself. The cost is one extra
    /// <c>$all</c> pass over an offline store, which is a cheap price for the last check before an
    /// irreversible swap.
    /// </remarks>
    public static async Task<Dictionary<string, long>> RecountSourceAsync(KurrentDBClient source,
        IReadOnlySet<string> reservedStreams, CancellationToken ct = default)
    {
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        var read = source.ReadAllAsync(Direction.Forwards, Position.Start, resolveLinkTos: false,
            cancellationToken: ct);
        await foreach (var re in read.ConfigureAwait(false))
        {
            var er = re.Event;
            if (er is null) continue;
            if (er.EventStreamId.Length > 0 && er.EventStreamId[0] == '$') continue;
            if (reservedStreams.Contains(er.EventStreamId)) continue;
            if (er.EventType.Length > 0 && er.EventType[0] == '$') continue;  // includes the "$>" link type
            counts.TryGetValue(er.EventStreamId, out var c);
            counts[er.EventStreamId] = c + 1;
        }
        return counts;
    }

    /// <summary>
    /// Compares the independent recount against the copy's own account of itself. An empty result means the
    /// swap may proceed; anything in it must abort the run with the old store untouched.
    /// </summary>
    public static IReadOnlyList<VerificationIssue> Check(CopyResult copy,
        IReadOnlyDictionary<string, long> trueSourceCounts, RuleReach rules)
    {
        ArgumentNullException.ThrowIfNull(copy);
        ArgumentNullException.ThrowIfNull(trueSourceCounts);
        ArgumentNullException.ThrowIfNull(rules);

        var issues = new List<VerificationIssue>();
        var streams = trueSourceCounts.Keys
            .Union(copy.SourceStreams.Keys, StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal);

        foreach (var stream in streams)
        {
            trueSourceCounts.TryGetValue(stream, out var actuallyThere);
            copy.SourceStreams.TryGetValue(stream, out var info);
            var engineSaw = info?.SourceCount ?? 0;

            // 1. The engine has to have SEEN everything that is in the source. This is the check the library's
            //    verifier structurally cannot make: a read that stops early, or a stream never enumerated,
            //    lowers its expectation by exactly as much as its result.
            if (engineSaw != actuallyThere)
            {
                issues.Add(new VerificationIssue(stream,
                    $"the source holds {actuallyThere} copyable event(s) but the copy engine read {engineSaw}"));
                continue; // the counts below are derived from a number already known to be wrong
            }

            if (info is null || actuallyThere == 0) continue;

            // NOTE: there is deliberately no "SourceCount == Kept + Dropped" check here. It reads like a
            // guard and is a TAUTOLOGY: CopyEngine increments SourceCount and then unconditionally exactly one
            // of Dropped or Kept, so it can never fail. A check that cannot fail is worse than no check —
            // it reports confidence it has not earned.

            // 2. A stream that vanished entirely must be attributable to a rule. Without this, a stream emptied
            //    by a bug is reported to the operator as an intended drop — and on a pure copy, where no rule
            //    can explain anything, that is exactly the silent total loss this tool must never cause.
            if (info.Kept == 0 && !rules.ExplainsLossOf(stream))
                issues.Add(new VerificationIssue(stream,
                    $"the whole stream ({info.SourceCount} event(s)) is absent from the new store and no rule "
                    + "accounts for it"));
        }

        return issues;
    }
}
