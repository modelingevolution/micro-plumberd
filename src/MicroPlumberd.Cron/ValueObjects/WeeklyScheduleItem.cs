namespace MicroPlumberd.Services.Cron;

public readonly record struct WeeklyScheduleItem(DayOfWeek Day, TimeOnly Time) : IComparable<WeeklyScheduleItem>, IComparer<WeeklyScheduleItem>
{
    public int CompareTo(WeeklyScheduleItem other)
    {
        var dayCompare = Day.CompareTo(other.Day);
        return dayCompare != 0 ? dayCompare : Time.CompareTo(other.Time);
    }

    public int Compare(WeeklyScheduleItem x, WeeklyScheduleItem y)
    {
        var dayCompare = x.Day.CompareTo(y.Day);
        return dayCompare != 0 ? dayCompare : x.Time.CompareTo(y.Time);
    }
}