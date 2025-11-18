using System.Text.Json.Serialization;

namespace MicroPlumberd.Services.Cron;

/// <summary>
/// Represents a schedule that runs at specific times each day.
/// </summary>
[JsonConverter(typeof(ScheduleJsonConverter<DailySchedule>))]
public class DailySchedule() : Schedule
{
    private SortedSet<TimeOnly> _items;

    /// <summary>
    /// Gets or sets the times of day when the job should run.
    /// </summary>
    public TimeOnly[] Items
    {
        get => _items.ToArray();
        init => _items = new(value);
    }

    /// <inheritdoc/>
    public override DateTime GetNextRunTime(DateTime currentTime)
    {
        // If no items, return "never"
        if (_items.Count == 0)
            return DateTime.MaxValue;

        // Handle case where current time is before StartTime
        if (StartTime.HasValue && currentTime < StartTime.Value)
        {
            DateTime startDate = StartTime.Value.Date;
            DateTime next = startDate.Add(_items.Min.ToTimeSpan()); // Earliest time on start date
            if (next < StartTime.Value)
                next = startDate.AddDays(1) + _items.Min.ToTimeSpan(); // Move to next day if too early
            return next;
        }

        // If past EndTime, no more runs
        if (EndTime.HasValue && currentTime >= EndTime.Value)
            return DateTime.MaxValue;

        DateTime currentDate = currentTime.Date;
        TimeOnly currentTimeOfDay = TimeOnly.FromTimeSpan(currentTime.TimeOfDay);

        // Use GetViewBetween to get times after currentTimeOfDay
        var view = _items.GetViewBetween(currentTimeOfDay, TimeOnly.MaxValue);
        if (view.Any())
        {
            DateTime nextRun = currentDate + view.Min.ToTimeSpan(); // Earliest time today after current time
            if (EndTime.HasValue && nextRun > EndTime.Value)
                return DateTime.MaxValue;
            return nextRun;
        }

        // No times left today, use first time tomorrow
        DateTime nextRunTomorrow = currentDate.AddDays(1) + _items.Min.ToTimeSpan();
        if (EndTime.HasValue && nextRunTomorrow > EndTime.Value)
            return DateTime.MaxValue;
        return nextRunTomorrow;
    }
}