namespace MicroPlumberd.Services;

/// <summary>
/// Selects where a catch-up read model sources its MERGED stream from. The default is
/// <see cref="Projection"/> (unchanged for every existing app); <see cref="UserDefinedIndex"/> is an opt-in,
/// additive path that reads a KurrentDB 26.1 user-defined index via filtered <c>$all</c> instead of a
/// <c>fromStreams(...).linkTo(outputStream)</c> join projection. See
/// <c>docs/design-index-backed-merge-streams.md</c>.
/// </summary>
public enum MergeSource
{
    /// <summary>The default: a <c>fromStreams(...).linkTo(outputStream)</c> join projection (unchanged).</summary>
    Projection = 0,

    /// <summary>
    /// Opt-in: a KurrentDB user-defined index read via filtered <c>$all</c>. Catch-up subscriptions ONLY —
    /// persistent subscriptions cannot see index links (SPIKE-7), so <c>persistently: true</c> is rejected.
    /// <para>
    /// <b>Guarantees:</b> no-loss and <b>commit ordering</b> (identical to the projection path), and
    /// <c>ICaughtUpHandler.CaughtUp()</c> DOES fire.
    /// </para>
    /// <para>
    /// <b>WEAKER CaughtUp semantics than projection-backed — read before opting in.</b> An index-backed
    /// subscription's history→live boundary tracks the <c>$all</c> position, so while the index is still
    /// BACKFILLING, <c>CaughtUp</c> can fire BEFORE all historical (backfilled) links have been delivered — they
    /// then arrive as "live". Unlike the projection output-stream path, <c>CaughtUp</c> here does NOT guarantee
    /// that all history was processed first. A read model that treats <c>ICaughtUpHandler.CaughtUp()</c> as a
    /// "fully caught up / now authoritative" readiness signal should stay on <see cref="Projection"/> (or must
    /// tolerate the weaker guarantee). Eventual delivery of all history and commit ordering are still guaranteed.
    /// </para>
    /// </summary>
    UserDefinedIndex = 1
}
