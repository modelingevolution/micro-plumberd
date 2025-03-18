using System.Text.Json.Serialization;

namespace MicroPlumberd.Services.Cron;

[JsonConverter(typeof(ScheduleJsonConverter))]
public abstract class Schedule
{
    public DateTime? StartTime { get; set; } // When the schedule begins (optional)
    public DateTime? EndTime { get; set; }   // When the schedule ends (optional)

    // Abstract method to compute the next run time
    public abstract DateTime GetNextRunTime(DateTime currentTime);
}