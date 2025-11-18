using System.Text.Json.Serialization;

namespace MicroPlumberd.Services.Cron;

/// <summary>
/// Represents a schedule that runs at regular time intervals.
/// </summary>
[JsonConverter(typeof(ScheduleJsonConverter<IntervalSchedule>))]
public class IntervalSchedule : Schedule
{
    /// <summary>
    /// Gets or sets the time interval between job executions.
    /// </summary>
    public TimeSpan Interval { get; init; }

    /// <inheritdoc/>
    public override DateTime GetNextRunTime(DateTime currentTime)
    {
        // If no start time, assume schedule starts at current time
        if (!StartTime.HasValue)
            return currentTime + Interval;

        if (currentTime < StartTime.Value)
            return StartTime.Value;

        // If past end time, no more runs
        if (EndTime.HasValue && currentTime >= EndTime.Value)
            return DateTime.MaxValue;

        TimeSpan elapsed = currentTime - StartTime.Value;
        long intervalsPassed = (long)(elapsed.Ticks / Interval.Ticks);
        DateTime next = StartTime.Value + TimeSpan.FromTicks(Interval.Ticks * (intervalsPassed + 1));

        // Check if next run exceeds EndTime
        if (EndTime.HasValue && next > EndTime.Value)
            return DateTime.MaxValue;

        return next;
    }
}