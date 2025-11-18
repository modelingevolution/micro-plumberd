using System.Text.Json.Serialization;

namespace MicroPlumberd.Services.Cron;

/// <summary>
/// Base class for job schedules that defines when a job should run.
/// </summary>
[JsonConverter(typeof(ScheduleJsonConverter<Schedule>))]
public abstract class Schedule
{
    /// <summary>
    /// Gets or sets the earliest date and time when the schedule becomes active.
    /// </summary>
    public virtual DateTime? StartTime { get; set; }

    /// <summary>
    /// Gets or sets the latest date and time when the schedule remains active.
    /// </summary>
    public virtual DateTime? EndTime { get; set; }

    /// <summary>
    /// Computes the next run time for the job based on the current time.
    /// </summary>
    /// <param name="currentTime">The current time to calculate from.</param>
    /// <returns>The next scheduled run time, or <see cref="DateTime.MaxValue"/> if no more runs are scheduled.</returns>
    public abstract DateTime GetNextRunTime(DateTime currentTime);
}

/// <summary>
/// Represents an empty schedule that never runs.
/// </summary>
[JsonConverter(typeof(ScheduleJsonConverter<EmptySchedule>))]
public class EmptySchedule : Schedule
{
    /// <inheritdoc/>
    public override DateTime GetNextRunTime(DateTime currentTime) => DateTime.MaxValue;

    /// <inheritdoc/>
    public override DateTime? StartTime
    {
        get => DateTime.MinValue; set{}
    }

    /// <inheritdoc/>
    public override DateTime? EndTime
    {
        get => DateTime.MinValue; set { }
    }
}
