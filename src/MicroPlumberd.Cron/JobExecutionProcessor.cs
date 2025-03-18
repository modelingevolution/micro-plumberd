using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using MicroPlumberd.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MicroPlumberd.Services.Cron;

[EventHandler]
public partial class JobExecutionProcessor(IPlumberInstance plumber, IServiceProvider sp, JobDefinitionModel model)
    : BackgroundService
{
    public readonly record struct RunningJob(Guid JobDefinitionId, JobId JobId, DateTimeOffset Created, string CommandType, JsonElement Command);
    public readonly struct ScheduledJob : IComparable<ScheduledJob>, IEquatable<ScheduledJob>
    {
        public ScheduledJob(Guid jobDefinitionId, DateTime startAt)
        {
            this.JobDefinitionId = jobDefinitionId;
            this.StartAt = startAt;
        }

        public Guid JobDefinitionId { get; }
        public DateTime StartAt { get; }
        


        public override int GetHashCode()
        {
            return JobDefinitionId.GetHashCode();
        }

        public void Deconstruct(out Guid jobDefinitionId, out DateTime startAt)
        {
            jobDefinitionId = this.JobDefinitionId;
            startAt = this.StartAt;
        }

        public int CompareTo(ScheduledJob other)
        {
            return JobDefinitionId.CompareTo(other.JobDefinitionId);
        }

        public bool Equals(ScheduledJob other)
        {
            return JobDefinitionId.Equals(other.JobDefinitionId);
        }

        public override bool Equals(object? obj)
        {
            return obj is ScheduledJob other && Equals(other);
        }

        public static bool operator ==(ScheduledJob left, ScheduledJob right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(ScheduledJob left, ScheduledJob right)
        {
            return !left.Equals(right);
        }
    }

    private readonly ConcurrentDictionary<Guid, RunningJob> _runningJobsByCommandId = new();
    private readonly SortedSet<ScheduledJob> _scheduledJobs = new();
    private readonly Channel<Func<Task>> _channel = Channel.CreateUnbounded<Func<Task>>();

    public record JobItem<T>(JobDefinitionModel.JobDefinition Definition, T? Info);

    public async Task<JobItem<ScheduledJob>[]> ScheduledItems(CancellationToken token = default)
    {
        JobItem<ScheduledJob>[] items = null;
        await Execute(() =>
        {
            items = _scheduledJobs.Select(x=> new JobItem<ScheduledJob>(model[x.JobDefinitionId], x)).ToArray();
            return Task.CompletedTask;
        }, token);
        return items;
    }
    public async Task<JobItem<RunningJob>[]> RunningItems(CancellationToken token = default)
    {
        JobItem<RunningJob>[] items = null;
        await Execute(() =>
        {
            items = _runningJobsByCommandId.Values.Select(x => new JobItem<RunningJob>(model[x.JobDefinitionId], x)).ToArray();
            return Task.CompletedTask;
        }, token);
        return items;
    }
    public async Task Execute(Func<Task> action, CancellationToken token)
    {
        using var m = new SemaphoreSlim(0);
        _channel.Writer.TryWrite(async () =>
        {
            await action();
            m.Release();
        });
        await m.WaitAsync(token);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _channel.Writer.TryWrite(async () => await OnStartup());
        _ = Task.Factory.StartNew(OnRun, stoppingToken, stoppingToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        return Task.CompletedTask;
    }
    private async Task OnJobExecutionStarted(JobId evJobId, Guid commandId, DateTimeOffset? created, string commandType, JsonElement command)
    {
        _runningJobsByCommandId.TryAdd(commandId, new RunningJob(evJobId.JobDefinitionId, evJobId, created ?? DateTimeOffset.Now, commandType, command));
    }
    private async Task OnRun(object? token)
    {
        CancellationToken stoppingToken = (CancellationToken)token!;
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        model.JobSchduleChanged += OnJobScheduleChanged;
        model.JobAvailabilityChanged += OnJobAvailabilityChanged;
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

    private async Task OnStartup()
    {
        var items = model.JobDefinitions;
        foreach (var item in items)
        {
            if (!item.IsEnabled) continue;

            var nx = item.Schedule.GetNextRunTime(DateTime.Now);
            _scheduledJobs.Add(new ScheduledJob(item.JobDefinitionId, nx));
        }
        await OnDispatchPendingItems();
    }
    private void OnJobScheduleChanged(object? sender, JobDefinitionModel.JobDefinition e)
    {
        _channel.Writer.TryWrite(async () =>
        {
            _scheduledJobs.Remove(new ScheduledJob(e.JobDefinitionId, DateTime.Today));
            await OnDispatchJob(e.JobDefinitionId);
        });
    }

    private void OnJobAvailabilityChanged(object? sender, JobDefinitionModel.JobDefinition e)
    {
        if (e.IsEnabled)
            _channel.Writer.TryWrite(async () => await OnDispatchJob(e.JobDefinitionId));
        else
        {
            _channel.Writer.TryWrite(async () =>
                _scheduledJobs.Remove(new ScheduledJob(e.JobDefinitionId, DateTime.Today)));
        }
    }

    private async Task Given(Metadata m, JobExecutionStarted ev) => _channel.Writer.TryWrite(async () =>
        await OnJobExecutionStarted(ev.JobId, ev.CommandId, m.Created()!, ev.CommandType, ev.Command));



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


        await OnDispatchPendingItems();
    }

    private async Task OnDispatchPendingItems()
    {
        while (_scheduledJobs.Count > 0 && _scheduledJobs.Min!.StartAt < DateTime.Now)
        {
            try
            {
                var j = _scheduledJobs.Min;
                await OnDispatchJob(j.JobDefinitionId, j.StartAt);
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

    private async Task<Guid> OnDispatchJob(Guid jobDefinitionId)
    {
        var job = model[jobDefinitionId];
        var at = job.Schedule.GetNextRunTime(DateTime.Now);
        return await OnDispatchJob(job, at);
    }
    private async Task<Guid> OnDispatchJob(Guid jobDefinitionId, DateTime at)
    {
        if (!model.TryGetValue(jobDefinitionId, out var job))
            throw new InvalidOperationException($"Job definition not found: {jobDefinitionId}");

        return await OnDispatchJob(job, at);
    }

    private async Task<Guid> OnDispatchJob(JobDefinitionModel.JobDefinition job, DateTime at)
    {
        var obj = JsonSerializer.Deserialize(job.Command, job.CommandType) ?? throw new InvalidOperationException($"Cannot deserialize command {job.CommandType.FullName}.");
        var cmdId = IdDuckTyping.Instance.TryGetGuidId(obj, out var commandId) ? commandId : throw new InvalidOperationException("No id on command");

        JobId id = new JobId(job.JobDefinitionId, at);
        var agg = await plumber.Get<JobAggregate>(id);

        using var scope = sp.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<ICommandBus>();
        await bus.QueueAsync(job.Recipient, obj, fireAndForget: true);
        agg.Start(id, cmdId, obj.GetType().AssemblyQualifiedName!, obj);
        //TODO: Should be chained.

        await plumber.SaveNew(agg);
        return cmdId;
    }
}