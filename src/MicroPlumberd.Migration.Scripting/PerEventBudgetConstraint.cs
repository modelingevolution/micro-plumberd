using System.Diagnostics;
using Jint;

namespace MicroPlumberd.Migration.Scripting;

/// <summary>
/// Caps the total JavaScript execution time spent on ONE event, across every helper and the
/// <c>transform</c> call together.
/// </summary>
/// <remarks>
/// <para>Jint's own <c>TimeoutInterval</c> is per <c>Execute</c>/<c>Invoke</c> entry and is RESET at each
/// one — with a <c>dropEvent</c>, two <c>update</c>s and a <c>transform</c> that is four separate budgets,
/// so a script could spend four times the limit on every event and never trip. This constraint is armed once
/// per event by <see cref="BeginEvent"/> and deliberately does NOT reset on re-entry, which is the whole
/// point: the budget belongs to the event, not to the call.</para>
/// <para><see cref="Check"/> is called by the interpreter on statement boundaries, so a tight
/// <c>while(true){}</c> is interrupted; it cannot interrupt a single non-yielding host operation.</para>
/// </remarks>
internal sealed class PerEventBudgetConstraint(TimeSpan budget) : Constraint
{
    private long _deadline = long.MaxValue;

    /// <summary>Arms a fresh budget for the event about to be processed.</summary>
    public void BeginEvent() =>
        _deadline = Stopwatch.GetTimestamp() + (long)(budget.TotalSeconds * Stopwatch.Frequency);

    /// <summary>Disarms the budget (no script is running).</summary>
    public void EndEvent() => _deadline = long.MaxValue;

    public override void Check()
    {
        if (Stopwatch.GetTimestamp() > _deadline)
            throw new TimeoutException(
                $"Script exceeded its per-event time budget of {budget.TotalSeconds:0.###}s.");
    }

    /// <summary>
    /// Intentionally a NO-OP. Jint resets constraints on every script entry; resetting here would restart the
    /// budget for each helper call and defeat the per-event cap.
    /// </summary>
    public override void Reset()
    {
    }
}
