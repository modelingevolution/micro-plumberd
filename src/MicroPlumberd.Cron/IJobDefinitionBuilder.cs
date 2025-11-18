namespace MicroPlumberd.Services.Cron;

/// <summary>
/// Provides a fluent interface for building and configuring job definitions.
/// </summary>
public interface IJobDefinitionBuilder
{
    /// <summary>
    /// Specifies the command to execute and its recipient when the job runs.
    /// </summary>
    /// <typeparam name="T">The type of command to execute.</typeparam>
    /// <typeparam name="D">The type of the recipient identifier, which must be parsable.</typeparam>
    /// <param name="command">The command instance to execute.</param>
    /// <param name="recipient">The recipient identifier that will handle the command.</param>
    /// <returns>The builder instance for method chaining.</returns>
    IJobDefinitionBuilder WithCommand<T,D>(T command, D recipient) where D : IParsable<D>;

    /// <summary>
    /// Specifies the schedule for the job.
    /// </summary>
    /// <param name="schedule">The schedule configuration.</param>
    /// <returns>The builder instance for method chaining.</returns>
    IJobDefinitionBuilder WithSchedule(Schedule schedule);

    /// <summary>
    /// Configures the job to run daily at specified times.
    /// </summary>
    /// <param name="items">The times of day when the job should run.</param>
    /// <returns>The builder instance for method chaining.</returns>
    IJobDefinitionBuilder WithDailySchedule(params TimeOnly[] items)
    {
        DailySchedule d = new DailySchedule() { Items = items };
        return WithSchedule(d);
    }

    /// <summary>
    /// Configures the job to run daily at specified times within an optional time window.
    /// </summary>
    /// <param name="startTime">The earliest date and time when the schedule becomes active.</param>
    /// <param name="endTime">The latest date and time when the schedule remains active.</param>
    /// <param name="items">The times of day when the job should run.</param>
    /// <returns>The builder instance for method chaining.</returns>
    IJobDefinitionBuilder WithDailySchedule(DateTime? startTime = null, DateTime? endTime = null, params TimeOnly[] items)
    {
        DailySchedule d = new DailySchedule() { Items = items, StartTime = startTime, EndTime = endTime };
        return WithSchedule(d);
    }

    /// <summary>
    /// Configures the job to run at regular intervals.
    /// </summary>
    /// <param name="interval">The time interval between job executions.</param>
    /// <param name="startTime">The earliest date and time when the schedule becomes active.</param>
    /// <param name="endTime">The latest date and time when the schedule remains active.</param>
    /// <returns>The builder instance for method chaining.</returns>
    IJobDefinitionBuilder WithIntervalSchedule(TimeSpan interval, DateTime? startTime = null, DateTime? endTime=null )
    {
        IntervalSchedule i = new IntervalSchedule() { StartTime = startTime, EndTime = endTime, Interval = interval };
        return WithSchedule(i);
    }

    /// <summary>
    /// Configures the job to run weekly at specified days and times.
    /// </summary>
    /// <param name="items">The weekly schedule items specifying days and times.</param>
    /// <returns>The builder instance for method chaining.</returns>
    IJobDefinitionBuilder WithWeeklySchedule(params WeeklyScheduleItem[] items)
    {
        WeeklySchedule i = new WeeklySchedule() { Items = items };
        return WithSchedule(i);
    }

    /// <summary>
    /// Configures the job to run weekly at specified days and times within an optional time window.
    /// </summary>
    /// <param name="startTime">The earliest date and time when the schedule becomes active.</param>
    /// <param name="endTime">The latest date and time when the schedule remains active.</param>
    /// <param name="items">The weekly schedule items specifying days and times.</param>
    /// <returns>The builder instance for method chaining.</returns>
    IJobDefinitionBuilder WithWeeklySchedule(DateTime? startTime = null, DateTime? endTime = null, params WeeklyScheduleItem[] items)
    {
        WeeklySchedule i = new WeeklySchedule() { Items = items, StartTime = startTime, EndTime = endTime };
        return WithSchedule(i);
    }

    /// <summary>
    /// Enables the job definition immediately after creation.
    /// </summary>
    /// <returns>The builder instance for method chaining.</returns>
    IJobDefinitionBuilder Enable();

    /// <summary>
    /// Creates and persists the job definition with the configured settings.
    /// </summary>
    /// <returns>A task representing the asynchronous operation, containing the created job definition aggregate.</returns>
    Task<JobDefinitionAggregate> Create();
}