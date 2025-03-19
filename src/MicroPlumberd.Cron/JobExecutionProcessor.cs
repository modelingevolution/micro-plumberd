using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using MicroPlumberd.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace MicroPlumberd.Services.Cron;

public static class Utils
{
    public static readonly Guid Max = Guid.AllBitsSet;

    public static IEnumerable<ScheduledJob> GetPending(this SortedSet<ScheduledJob> items, DateTime when)
    {
        ScheduledJob start = new ScheduledJob(Guid.Empty, DateTime.MinValue);
        ScheduledJob end = new ScheduledJob(Max, when);
        return items.GetViewBetween(start, end);
    }

    public static bool TryGetFollowing(this SortedSet<ScheduledJob> items, ScheduledJob start, out ScheduledJob result)
    {
        ScheduledJob end = new ScheduledJob(Max, DateTime.MaxValue);
        result = items.GetViewBetween(start, end).Skip(1).FirstOrDefault();
        return result != ScheduledJob.Empty;
    }
}

[EventHandler]
public partial class JobExecutionProcessor(IPlumberInstance plumber, IServiceProvider sp, JobDefinitionModel model) : IJobItemsMonitor
{
    private readonly ConcurrentDictionary<Guid, RunningJob> _runningJobsByCommandIdLookup = new();
    private readonly ConcurrentDictionary<Guid, RunningJob> _runningJobsByDefId = new();

    private readonly SortedSet<ScheduledJob> _scheduledJobs = new(ScheduledJob.TimeComparer);
    private readonly SortedSet<ScheduledJob> _manualQueue = new(ScheduledJob.TimeComparer);

    private readonly Channel<Func<Task>> _channel = Channel.CreateUnbounded<Func<Task>>();

    public event EventHandler Changed;

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
            items = _runningJobsByDefId.Values.Select(x => new JobItem<RunningJob>(model[x.JobDefinitionId], x)).ToArray();
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

    public Task StartAsync(CancellationToken stoppingToken)
    {
        _channel.Writer.TryWrite(async () => await OnStartup());
        _ = Task.Factory.StartNew(OnRun, stoppingToken, stoppingToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        return Task.CompletedTask;
    }
    private async Task OnJobExecutionStarted(JobId evJobId, Guid commandId, DateTimeOffset? created, string commandType, JsonElement command)
    {
        if(_runningJobsByDefId.TryGetValue(evJobId.JobDefinitionId, out var r))
            _runningJobsByCommandIdLookup.TryAdd(commandId, r);
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
                var ch = Changed;
                if (ch != null!)
                    _ = Task.Run(() => ch?.Invoke(this, EventArgs.Empty), stoppingToken);
            }
        }
        catch (OperationCanceledException ex)
        {
            // do nothing.
        }
    }

    private async Task CancelWakeUp()
    {
        if(_cts != null)
            await _cts.CancelAsync();
        _cts = null;
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
        await OnProcessScheduledJobs();
    }
    private void OnJobScheduleChanged(object? sender, JobDefinition e)
    {
        _channel.Writer.TryWrite(async () =>
        {
            _scheduledJobs.Remove(new ScheduledJob(e.JobDefinitionId, DateTime.Today));
            await OnScheduleAndDispatch(e.JobDefinitionId, ScheduleTrigger.Engine);
            await CancelWakeUp();
            await OnProcessScheduledJobs();
        });
    }

    private void OnJobAvailabilityChanged(object? sender, JobDefinition e)
    {
        if (e.IsEnabled)
            _channel.Writer.TryWrite(async () =>
            {
                await CancelWakeUp();
                await OnScheduleAndDispatch(e.JobDefinitionId, ScheduleTrigger.Engine);
                await OnProcessScheduledJobs();
            });
        else
        {
            _channel.Writer.TryWrite(async () =>
            {
                _scheduledJobs.Remove(new ScheduledJob(e.JobDefinitionId, DateTime.Today));
                await CancelWakeUp();
                await OnProcessScheduledJobs();
            });
        }
    }

    private async Task Given(Metadata m, JobExecutionStarted ev) => _channel.Writer.TryWrite(async () =>
        await OnJobExecutionStarted(ev.JobId, ev.CommandId, m.Created()!, ev.CommandType, ev.Command));


    private async Task Given(Metadata m, JobRunOnceEnqued ev)
    {
        _channel.Writer.TryWrite(async () =>
        {
            _manualQueue.Add(new ScheduledJob(ev.JobDefinitionId, DateTime.Now));
        });
    }
    private async Task Given(Metadata m, StartJobExecutionExecuted ev)
    {
        var cmdId = ev.CommandId;
        _channel.Writer.TryWrite(async () => await OnStartJobExecutionExecuted(cmdId));
    }

    private async Task OnStartJobExecutionExecuted(Guid cmdId)
    {
        if (_runningJobsByCommandIdLookup.TryRemove(cmdId, out var job))
        {
            if (model.TryGetValue(job.JobDefinitionId, out var jobDef) && jobDef.IsEnabled)
            {
                var nx = jobDef.Schedule.GetNextRunTime(DateTime.Now);
                _scheduledJobs.Add(new ScheduledJob(job.JobDefinitionId, nx)); // we add only once the same job. SortedSet is only by JobDefinitionId.
            }
        }


        await OnProcessScheduledJobs();
    }

    private CancellationTokenSource? _cts;
    private async Task OnProcessScheduledJobs(int h = 0)
    {
        
        var manuallyScheduled = _manualQueue.GetPending(DateTime.Now).ToImmutableArray();
        foreach(var item in manuallyScheduled)
        {
            if (this._runningJobsByDefId.ContainsKey(item.JobDefinitionId)) continue;
            if (!_runningJobsByDefId.ContainsKey(item.JobDefinitionId))
            {
                await OnDispatchJob(item.JobDefinitionId, item.StartAt, ScheduleTrigger.Manual);
                manuallyScheduled.Remove(item);
            }
        }
        var scheduled = _scheduledJobs.GetPending(DateTime.Now).ToImmutableArray();
        foreach (var item in scheduled)
        {
            if(this._runningJobsByDefId.ContainsKey(item.JobDefinitionId)) continue;
            try
            {
                await OnDispatchJob(item.JobDefinitionId, item.StartAt, ScheduleTrigger.Engine);
                _scheduledJobs.Remove(item);
            }
            catch (Exception ex)
            {
                _scheduledJobs.Remove(_scheduledJobs.Min);
                var app = await plumber.Get<JobDefinitionAggregate>(_scheduledJobs.Min.JobDefinitionId);
                app.Disable(ex.Message);

                await plumber.SaveChanges(app);
            }
        }

        if (_scheduledJobs.Any() && _scheduledJobs.TryGetFollowing(scheduled.Last(), out var f))
        {
            TimeSpan d = DateTimeOffset.Now - f.StartAt;
            d = d.Add(TimeSpan.FromSeconds(1));
            if (d > TimeSpan.Zero)
                ConfigureNextIterationWakeup(d);
            else if (h < 10)
                this._channel.Writer.TryWrite(async () => await OnProcessScheduledJobs(h + 1));
            else throw new InvalidOperationException("Cannot configure next iteration wakeup.");
        }
    }

    private void ConfigureNextIterationWakeup(TimeSpan d)
    {
        _cts = new CancellationTokenSource(d);
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(d, _cts.Token);
                _channel.Writer.TryWrite(async () => await OnProcessScheduledJobs());
            }
            catch (OperationCanceledException)
            {
                // do nothing.
            }
        });
    }

    private async Task<Guid?> OnScheduleAndDispatch(Guid jobDefinitionId, ScheduleTrigger trigger)
    {
        var job = model[jobDefinitionId];
        var at = job.Schedule.GetNextRunTime(DateTime.Now);
        return await OnDispatchJob(job, at, trigger);
    }
    private async Task<Guid?> OnDispatchJob(Guid jobDefinitionId, DateTime at, ScheduleTrigger trigger)
    {
        if (!model.TryGetValue(jobDefinitionId, out var job))
            throw new InvalidOperationException($"Job definition not found: {jobDefinitionId}");

        return await OnDispatchJob(job, at, trigger);
    }

    private async Task<Guid?> OnDispatchJob(JobDefinition job, DateTime at, ScheduleTrigger trigger)
    {
        var obj = JsonSerializer.Deserialize(job.Command, job.CommandType) ?? throw new InvalidOperationException($"Cannot deserialize command {job.CommandType.FullName}.");
        var cmdId = IdDuckTyping.Instance.TryGetGuidId(obj, out var commandId) ? commandId : throw new InvalidOperationException("No id on command");

        JobId id = new JobId(job.JobDefinitionId, at);
        var agg = await plumber.Get<JobAggregate>(id);

        using var scope = sp.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<ICommandBus>();

        if (!_runningJobsByDefId.TryAdd(job.JobDefinitionId,
                new RunningJob(job.JobDefinitionId, id, DateTimeOffset.Now, obj.GetType().AssemblyQualifiedName!,
                    job.Command, trigger)))
            return Guid.Empty;

        agg.Start(id, cmdId, obj.GetType().AssemblyQualifiedName!, obj);
        await plumber.SaveNew(agg);

        await bus.QueueAsync(job.Recipient, obj, fireAndForget: true);
        
        
        return cmdId;
    }
}