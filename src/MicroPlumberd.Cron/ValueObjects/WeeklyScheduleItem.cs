namespace MicroPlumberd.Services.Cron;

/// <summary>
/// Represents a specific day and time within a weekly schedule.
/// </summary>
/// <param name="Day">The day of the week.</param>
/// <param name="Time">The time of day.</param>
public readonly record struct WeeklyScheduleItem(DayOfWeek Day, TimeOnly Time) : IComparable<WeeklyScheduleItem>, IComparer<WeeklyScheduleItem>
{
    /// <inheritdoc/>
    public int CompareTo(WeeklyScheduleItem other)
    {
        var dayCompare = Day.CompareTo(other.Day);
        return dayCompare != 0 ? dayCompare : Time.CompareTo(other.Time);
    }

    /// <inheritdoc/>
    public int Compare(WeeklyScheduleItem x, WeeklyScheduleItem y)
    {
        var dayCompare = x.Day.CompareTo(y.Day);
        return dayCompare != 0 ? dayCompare : x.Time.CompareTo(y.Time);
    }
}