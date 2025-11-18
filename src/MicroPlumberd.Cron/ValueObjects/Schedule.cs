using System.Text.Json.Serialization;

namespace MicroPlumberd.Services.Cron;

[JsonConverter(typeof(ScheduleJsonConverter<Schedule>))]
public abstract class Schedule
{
    public virtual DateTime? StartTime { get; set; } // When the schedule begins (optional)
    public virtual DateTime? EndTime { get; set; }   // When the schedule ends (optional)

    // Abstract method to compute the next run time
    public abstract DateTime GetNextRunTime(DateTime currentTime);
}
[JsonConverter(typeof(ScheduleJsonConverter<EmptySchedule>))]
public class EmptySchedule : Schedule
{
    public override DateTime GetNextRunTime(DateTime currentTime) => DateTime.MaxValue;
    public override DateTime? StartTime
    {
        get => DateTime.MinValue; set{}
    }
    public override DateTime? EndTime
    {
        get => DateTime.MinValue; set { }
    }
}
