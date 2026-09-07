using System.Collections.Immutable;

namespace MicroPlumberd.Migration;

/// <summary>Overall state of a migration run.</summary>
public enum MigrationRunState
{
    /// <summary>The run has not started yet.</summary>
    Pending,

    /// <summary>The run is in progress.</summary>
    Running,

    /// <summary>The run finished successfully.</summary>
    Completed,

    /// <summary>The run threw and did not finish.</summary>
    Failed
}

/// <summary>The current phase of a running migration.</summary>
public enum MigrationPhase
{
    /// <summary>Nothing has started.</summary>
    NotStarted,

    /// <summary>Reading applied history and computing the pending set.</summary>
    Planning,

    /// <summary>Pre-creating the app's join projections on the empty dest (before the event copy).</summary>
    ProjectionCopy,

    /// <summary>Copying source events to the dest in $all order (paced so the projections stay caught up).</summary>
    EventCopy,

    /// <summary>Final drain of the join projections to head.</summary>
    Draining,

    /// <summary>Appending the migration-history records to the dest.</summary>
    RecordingHistory,

    /// <summary>Cross-checking the dest against the copy bookkeeping.</summary>
    Verifying,

    /// <summary>The run finished successfully.</summary>
    Completed,

    /// <summary>The run failed.</summary>
    Failed
}

/// <summary>Lifecycle state of a single migration within a run.</summary>
public enum MigrationItemState
{
    /// <summary>Defined but not yet applied (or applied in a previous run and carried forward — see below).</summary>
    Pending,

    /// <summary>Being applied in the current run (pending migrations are applied together in one $all pass).</summary>
    Running,

    /// <summary>Applied — either recorded in this run or already present in the source history.</summary>
    Completed,

    /// <summary>The run failed while this migration was being applied.</summary>
    Failed
}

/// <summary>Per-migration status. Serializable; safe to hand to a REST layer.</summary>
/// <param name="Id">The migration id (ordering key).</param>
/// <param name="Name">The migration's human name.</param>
/// <param name="State">Its lifecycle state in this run.</param>
/// <param name="StartedUtc">When it entered <see cref="MigrationItemState.Running"/> (null if never).</param>
/// <param name="FinishedUtc">When it reached <see cref="MigrationItemState.Completed"/>/<see cref="MigrationItemState.Failed"/> (null if never).</param>
/// <param name="Error">The failure message, if it failed.</param>
public sealed record MigrationItemStatus(
    string Id,
    string Name,
    MigrationItemState State,
    DateTime? StartedUtc,
    DateTime? FinishedUtc,
    string? Error);

/// <summary>
/// Progress of the event-copy phase. Progress is measured by <c>$all</c> commit position (the position of the
/// last COPYABLE event is captured up front) rather than a pre-counted total — counting every source event
/// would require a second full <c>$all</c> scan of the whole store. The denominator excludes the trailing
/// <c>$</c>-system / <c>$&gt;</c> merge-link events the copy skips, so the percentage reaches 100 when the last
/// copyable event is processed. <see cref="EventsCopied"/> is the running count of user events written so far.
/// </summary>
/// <param name="EventsCopied">User events written to the dest so far.</param>
/// <param name="CurrentSourceStream">The source stream of the event most recently processed.</param>
/// <param name="CurrentCommitPosition">The <c>$all</c> commit position most recently processed.</param>
/// <param name="HeadCommitPosition">The <c>$all</c> commit position of the last copyable source event, captured before the copy.</param>
public sealed record EventCopyProgress(
    long EventsCopied,
    string? CurrentSourceStream,
    ulong CurrentCommitPosition,
    ulong HeadCommitPosition)
{
    /// <summary>0–100, from commit position vs the head captured up front. 100 when the head is 0 (empty source).</summary>
    public double PercentComplete => HeadCommitPosition == 0
        ? 100d
        : Math.Min(100d, CurrentCommitPosition * 100d / HeadCommitPosition);
}

/// <summary>
/// An immutable, serializable snapshot of a migration run's status + per-phase progress. Safe to read
/// concurrently while the run proceeds (each read returns a fresh immutable instance).
/// </summary>
/// <param name="RunState">Overall run state.</param>
/// <param name="Phase">The current phase.</param>
/// <param name="DryRun">Whether this is a dry run (nothing is written).</param>
/// <param name="RunningMigrationId">
/// Representative id of the pending migrations being applied while running (the tool applies all pending
/// migrations together in a single <c>$all</c> pass, so this is the first pending id; null when not running or
/// nothing is pending). The authoritative per-migration view is <paramref name="Migrations"/>.
/// </param>
/// <param name="Migrations">Every migration (carried-forward + pending) with its per-item state.</param>
/// <param name="EventCopy">Event-copy progress (null before the copy starts).</param>
/// <param name="StartedUtc">When the run started.</param>
/// <param name="FinishedUtc">When the run finished (null while running).</param>
/// <param name="Error">The run's failure message, if it failed.</param>
public sealed record MigrationStatusSnapshot(
    MigrationRunState RunState,
    MigrationPhase Phase,
    bool DryRun,
    string? RunningMigrationId,
    ImmutableArray<MigrationItemStatus> Migrations,
    EventCopyProgress? EventCopy,
    DateTime? StartedUtc,
    DateTime? FinishedUtc,
    string? Error)
{
    /// <summary>The initial "nothing has happened yet" snapshot.</summary>
    public static readonly MigrationStatusSnapshot Empty = new(
        MigrationRunState.Pending, MigrationPhase.NotStarted, false, null,
        ImmutableArray<MigrationItemStatus>.Empty, null, null, null, null);
}
