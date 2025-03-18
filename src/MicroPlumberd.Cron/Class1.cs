using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using MicroPlumberd.Utils;
using Microsoft.Extensions.Hosting;

namespace MicroPlumberd.Cron
{
    public class ScheduleJsonConverter : JsonConverter<Schedule>
    {
        public override Schedule Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            // Parse the JSON into a JsonDocument to inspect the "type" property
            using JsonDocument doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;

            // Check for the "type" discriminator property
            if (!root.TryGetProperty("type", out var typeProp))
                throw new JsonException("Missing 'type' property in schedule JSON.");

            string? typeStr = typeProp.GetString();
            if (string.IsNullOrEmpty(typeStr))
                throw new JsonException("The 'type' property cannot be null or empty.");

            // Deserialize to the appropriate derived type based on "type"
            return typeStr switch
            {
                "Interval" => JsonSerializer.Deserialize<IntervalSchedule>(root.GetRawText(), options)
                               ?? throw new JsonException("Failed to deserialize IntervalSchedule."),
                "Daily" => JsonSerializer.Deserialize<DailySchedule>(root.GetRawText(), options)
                           ?? throw new JsonException("Failed to deserialize DailySchedule."),
                "Weekly" => JsonSerializer.Deserialize<WeeklySchedule>(root.GetRawText(), options)
                            ?? throw new JsonException("Failed to deserialize WeeklySchedule."),
                _ => throw new JsonException($"Unknown schedule type: {typeStr}")
            };
        }

        public override void Write(Utf8JsonWriter writer, Schedule value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();

            // Write the "type" discriminator based on the concrete type
            string typeStr = value switch
            {
                IntervalSchedule _ => "Interval",
                DailySchedule _ => "Daily",
                WeeklySchedule _ => "Weekly",
                _ => throw new JsonException($"Unknown schedule type: {value.GetType().Name}")
            };
            writer.WriteString("type", typeStr);

            // Write common properties from the base Schedule class
            writer.WriteString("StartTime", value.StartTime?.ToString("O")); // ISO 8601 format
            writer.WriteString("EndTime", value.EndTime?.ToString("O"));

            // Write type-specific properties
            switch (value)
            {
                case IntervalSchedule interval:
                    writer.WriteString("Interval", interval.Interval.ToString());
                    break;

                case DailySchedule daily:
                    writer.WritePropertyName("Items");
                    JsonSerializer.Serialize(writer, daily.Items, options);
                    break;

                case WeeklySchedule weekly:
                    writer.WritePropertyName("Items");
                    JsonSerializer.Serialize(writer, weekly.Item, options); // Note: 'Item' property
                    break;
            }

            writer.WriteEndObject();
        }
    }
    [JsonConverter(typeof(ScheduleJsonConverter))]
    public abstract class Schedule
    {
        public DateTime? StartTime { get; set; } // When the schedule begins (optional)
        public DateTime? EndTime { get; set; }   // When the schedule ends (optional)

        // Abstract method to compute the next run time
        public abstract DateTime GetNextRunTime(DateTime currentTime);
    }
    

    public class IntervalSchedule : Schedule
    {
        public TimeSpan Interval { get; init; } // e.g., Minutes

        public override DateTime GetNextRunTime(DateTime currentTime)
        {
            // If no start time, assume schedule starts at current time
            if (!StartTime.HasValue)
                return currentTime + Interval;

            if (currentTime < StartTime.Value)
                return StartTime.Value;

            // If past end time, no more runs
            if (EndTime.HasValue && currentTime >= EndTime.Value)
                return DateTime.MaxValue;

            TimeSpan elapsed = currentTime - StartTime.Value;
            long intervalsPassed = (long)(elapsed.Ticks / Interval.Ticks);
            DateTime next = StartTime.Value + TimeSpan.FromTicks(Interval.Ticks * (intervalsPassed + 1));

            // Check if next run exceeds EndTime
            if (EndTime.HasValue && next > EndTime.Value)
                return DateTime.MaxValue;

            return next;
        }
    }
    public class DailySchedule : Schedule
    {
        private SortedSet<TimeOnly> _items;
        public TimeOnly[] Items
        {
            get => _items.ToArray();
            init => _items = new(value);
        } // e.g., 09:00:00

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
            TimeOnly currentTimeOfDay = TimeOnly .FromTimeSpan(currentTime.TimeOfDay);

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


public readonly record struct WeeklyScheduleItem(DayOfWeek Day, TimeOnly Time) : IComparer<WeeklyScheduleItem>
{
    public int Compare(WeeklyScheduleItem x, WeeklyScheduleItem y)
    {
        var dayCompare = x.Day.CompareTo(y.Day);
        return dayCompare != 0 ? dayCompare : x.Time.CompareTo(y.Time);
    }
}

public class WeeklySchedule : Schedule
{
        public WeeklyScheduleItem[] Item
        {
            get => _items.ToArray(); 
            set => _items = new(value);
        }
        private SortedSet<WeeklyScheduleItem> _items;

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

    public class ScheduleFactory
    {

    }

    public partial class JobExecutionModel
    {
        public record JobDefinition
        {
            public Guid JobDefinitionId { get; init; }
            public Schedule Schedule { get; set; }
            public JsonObject Command { get; set; }
            public string Recipient { get; set; }
            public Type CommandType { get; set; }
            public bool IsEnabled { get; set; }
        }
        private readonly ConcurrentDictionary<Guid, JobDefinition> _jobDefinitions = new();
        public bool TryGetValue(Guid id, out JobDefinition job) => _jobDefinitions.TryGetValue(id, out job);
        private async Task Given(Metadata m, JobEnabled ev)
        {

        }
        private async Task Given(Metadata m, JobDisabled ev)
        {

        }
        private async Task Given(Metadata m, JobNamed ev)
        {

        }
        private async Task Given(Metadata m, JobScheduleDefined ev)
        {

        }
        private async Task Given(Metadata m, JobProcessDefined ev)
        {

        }
    }



    [EventHandler]
    public partial class JobExecutionProcessor(IPlumber plumber, ICommandBus bus, JobExecutionModel model)
        : BackgroundService
    {
        record RunningJob(Guid JobDefinitionId, JobId JobId, DateTimeOffset Created, string CommandType, object Command);
        record ScheduledJob(Guid JobDefinitionId, DateTime StartAt) : IComparable<ScheduledJob>
        {
            public int CompareTo(ScheduledJob? other)
            {
                if (ReferenceEquals(this, other)) return 0;
                if (other is null) return 1;
                return JobDefinitionId.CompareTo(other.JobDefinitionId);
            }
        }

        private readonly ConcurrentDictionary<Guid, RunningJob> _runningJobsByCommandId = new ();
        private readonly SortedSet<ScheduledJob> _scheduledJobs = new ();
        private readonly Channel<Func<Task>> _channel = Channel.CreateUnbounded<Func<Task>>();

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _ = Task.Factory.StartNew(OnRun, stoppingToken, stoppingToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            return Task.CompletedTask;
        }
        private async Task OnJobExecutionStarted(JobId evJobId, Guid commandId, DateTimeOffset? created,string commandType, JsonObject command)
        {
            _runningJobsByCommandId.TryAdd(commandId, new RunningJob(evJobId.JobDefinitionId, evJobId, created ?? DateTimeOffset.Now, commandType, command));
        }
        private async Task OnRun(object? token)
        {
            CancellationToken stoppingToken = (CancellationToken)token!;
            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    var action = await _channel.Reader.ReadAsync(stoppingToken);
                    await action();
                }
            }
            catch (OperationCanceledException ex)
            {
                // do nothing.
            }
        }

        private async Task Given(Metadata m, JobExecutionStarted ev) => _channel.Writer.TryWrite(async () => 
            await OnJobExecutionStarted(ev.JobId, ev.CommandId, m.Created()!,ev.CommandType, ev.Command));

        

        private async Task Given(Metadata m, StartJobExecutionExecuted ev)
        {
            var cmdId = ev.CommandId;
            _channel.Writer.TryWrite(async () => await OnStartJobExecutionExecuted(cmdId));
        }

        private async Task OnStartJobExecutionExecuted(Guid cmdId)
        {
            if (_runningJobsByCommandId.TryRemove(cmdId, out var job))
            {
                if (model.TryGetValue(job.JobDefinitionId, out var jobDef) && jobDef.IsEnabled)
                {
                    var nx = jobDef.Schedule.GetNextRunTime(DateTime.Now);
                    _scheduledJobs.Add(new ScheduledJob(job.JobDefinitionId, nx)); // we add only once the same job. SortedSet is only by JobDefinitionId.
                }
            }


            while (_scheduledJobs.Count > 0 && _scheduledJobs.Min!.StartAt < DateTime.Now)
            {
                try
                {
                    var j = _scheduledJobs.Min;
                    await DispatchJob(j.JobDefinitionId, j.StartAt);
                    _scheduledJobs.Remove(j);
                }
                catch (Exception ex)
                {
                    var app = await plumber.Get<JobDefinitionAggregate>(_scheduledJobs.Min.JobDefinitionId);
                    app.Disable(ex.Message);

                    await plumber.SaveChanges(app);
                }

            }
        }

        private async Task<Guid> DispatchJob(Guid jobDefinitionId, DateTime at)
        {
            if (!model.TryGetValue(jobDefinitionId, out var job))
                throw new InvalidOperationException($"Job definition not found: {jobDefinitionId}");
            var obj = JsonSerializer.Deserialize(job.Command, job.CommandType) ?? throw new InvalidOperationException($"Cannot deserialize command {job.CommandType.FullName}.");
            var cmdId = IdDuckTyping.Instance.TryGetGuidId(obj, out var commandId) ? commandId : throw new InvalidOperationException("No id on command");

            JobId id = new JobId(job.JobDefinitionId, at);
            var agg = await plumber.Get<JobAggregate>(id);
            
            await bus.QueueAsync(job.Recipient, obj, fireAndForget: true);
            agg.Start(id, cmdId, obj.GetType().AssemblyQualifiedName, job.Command);
            //TODO: Should be chained.

            await plumber.SaveNew(agg);
            return cmdId;
        }

        
    }


    public record StartJobExecutionExecuted
    {
        public Guid Id { get; init; } = Guid.NewGuid();

        [DataMember(Order = 1)]
        public Guid CommandId { get; set; }

        [DataMember(Order = 2)]
        public TimeSpan Duration { get; set; }
    }
    public record StartJobExecution
    {
        public Guid Id { get; init; } = Guid.NewGuid();
    }
    public record CancelJobExecution
    {
        public Guid Id { get; init; } = Guid.NewGuid();
    }
    
    public record JobExecutionStarted
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public JobId JobId { get; init; }
        public Guid CommandId { get; init; }
        public JsonObject Command { get; init; }
        public string CommandType { get; init; }
    }

    public record JobExecutionCompleted
    {
        public Guid Id { get; init; } = Guid.NewGuid();
    }

    public record JobExecutionFailed
    {
        public Guid Id { get; init; } = Guid.NewGuid();
    }
}
