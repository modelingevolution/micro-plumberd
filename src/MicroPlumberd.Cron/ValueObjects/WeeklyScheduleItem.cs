namespace MicroPlumberd.Services.Cron;

public readonly record struct WeeklyScheduleItem(DayOfWeek Day, TimeOnly Time) : IComparer<WeeklyScheduleItem>
{
    public int Compare(WeeklyScheduleItem x, WeeklyScheduleItem y)
    {
        var dayCompare = x.Day.CompareTo(y.Day);
        return dayCompare != 0 ? dayCompare : x.Time.CompareTo(y.Time);
    }
}