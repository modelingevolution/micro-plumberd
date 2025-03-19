namespace MicroPlumberd.Services.Cron;

public interface IJobDefinitionBuilder
{
    IJobDefinitionBuilder WithCommand<T,D>(T command, D recipient) where D : IParsable<D>;
    IJobDefinitionBuilder WithSchedule(Schedule schedule);

    IJobDefinitionBuilder WithDailySchedule(params TimeOnly[] items)
    {
        DailySchedule d = new DailySchedule() { Items = items };
        return WithSchedule(d);
    }
    IJobDefinitionBuilder WithDailySchedule(DateTime? startTime = null, DateTime? endTime = null, params TimeOnly[] items)
    {
        DailySchedule d = new DailySchedule() { Items = items, StartTime = startTime, EndTime = endTime };
        return WithSchedule(d);
    }
    IJobDefinitionBuilder WithIntervalSchedule(TimeSpan interval, DateTime? startTime = null, DateTime? endTime=null )
    {
        IntervalSchedule i = new IntervalSchedule() { StartTime = startTime, EndTime = endTime, Interval = interval };
        return WithSchedule(i);
    }

    IJobDefinitionBuilder WithWeeklySchedule(params WeeklyScheduleItem[] items)
    {
        WeeklySchedule i = new WeeklySchedule() { Items = items };
        return WithSchedule(i);
    }
    IJobDefinitionBuilder WithWeeklySchedule(DateTime? startTime = null, DateTime? endTime = null, params WeeklyScheduleItem[] items)
    {
        WeeklySchedule i = new WeeklySchedule() { Items = items, StartTime = startTime, EndTime = endTime };
        return WithSchedule(i);
    }

    IJobDefinitionBuilder Enable();
    Task<JobDefinitionAggregate> Create();
}