using System.Text.Json.Serialization;

namespace MicroPlumberd.Services.Cron;

/// <summary>
/// Represents a schedule that runs at specific days and times each week.
/// </summary>
[JsonConverter(typeof(ScheduleJsonConverter<WeeklySchedule>))]
public class WeeklySchedule : Schedule
{
    /// <summary>
    /// Gets or sets the weekly schedule items defining days and times.
    /// </summary>
    public WeeklyScheduleItem[] Items
    {
        get => _items.ToArray();
        set => _items = new(value);
    }
    private SortedSet<WeeklyScheduleItem> _items;

    /// <inheritdoc/>
    public override DateTime GetNextRunTime(DateTime currentTime)
    {
        if (_items.Count == 0)
            return DateTime.MaxValue;

        // Adjust base time if before StartTime
        DateTime baseTime = StartTime.HasValue && currentTime < StartTime.Value ? StartTime.Value : currentTime;

        if (EndTime.HasValue && currentTime >= EndTime.Value)
            return DateTime.MaxValue;

        DateTime currentDate = baseTime.Date;
        int currentDayNum = (int)baseTime.DayOfWeek;
        var currentTimeOfDay = TimeOnly.FromTimeSpan(baseTime.TimeOfDay);

        DateTime minFutureTime = DateTime.MaxValue;
        foreach (var item in _items)
        {
            int itemDayNum = (int)item.Day;
            int daysUntil = ((itemDayNum - currentDayNum + 7) % 7); // Days until next occurrence
            DateTime date = currentDate.AddDays(daysUntil);
            DateTime runTime = date + item.Time.ToTimeSpan();

            // If same day and time has passed, move to next week
            if (daysUntil == 0 && item.Time <= currentTimeOfDay)
                runTime = date.AddDays(7) + item.Time.ToTimeSpan();

            if (runTime > baseTime && runTime < minFutureTime)
                minFutureTime = runTime;
        }

        if (minFutureTime == DateTime.MaxValue || (EndTime.HasValue && minFutureTime > EndTime.Value))
            return DateTime.MaxValue;

        return minFutureTime;
    }
}