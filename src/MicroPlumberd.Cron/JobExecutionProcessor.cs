using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using MicroPlumberd.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MicroPlumberd.Services.Cron;

public static class Utils
{
    
    
    public static IEnumerable<ScheduledJob> GetPending(this SortedSet<ScheduledJob> items, DateTime when)
    {
        ScheduledJob start = ScheduledJob.Min(DateTime.MinValue);
        ScheduledJob end = ScheduledJob.Max(when);

        return items.GetViewBetween(start, end);
    }

}

[EventHandler]
public partial class JobExecutionProcessor(IPlumberInstance plumber, IServiceProvider sp, JobDefinitionModel model, ILogger<JobExecutionProcessor> log) : IJobsScheduler
{
    public event EventHandler<ScheduledJob>? JobScheduled;
    public event EventHandler<ScheduledJob>? JobRemovedFromSchedule;
    public event EventHandler<RunningJob>? RunningJobStarted;
    public event EventHandler<RunningJob>? RunningJobCompleted;

    private readonly ConcurrentDictionary<Guid, RunningJob> _runningJobsByCommandIdLookup = new();
    private readonly ConcurrentDictionary<Guid, RunningJob> _runningJobsByDefId = new();

    private readonly SortedSet<ScheduledJob> _scheduledJobs = new();
    private readonly SortedSet<ScheduledJob> _manualQueue = new();

    private readonly Channel<Func<Task>> _chIn = Channel.CreateUnbounded<Func<Task>>();
    private readonly Channel<Action> _chOut = Channel.CreateUnbounded<Action>();

    

    private bool AddScheduled(ScheduledJob job, SortedSet<ScheduledJob> items)
    {
        if (!items.Add(job)) return false;
        _chOut.Writer.TryWrite(() => JobScheduled?.Invoke(this, job));
        return true;
    }

    private bool RemoveFromSchedule(ScheduledJob job, SortedSet<ScheduledJob> items)
    {
        if (!items.Remove(job)) return false;
        _chOut.Writer.TryWrite(() => JobRemovedFromSchedule?.Invoke(this, job));
        return true;
    }

    private bool AddRunningJob(RunningJob job, Guid startedBy)
    {
        if (startedBy == Guid.Empty)
            return false;

        if (!_runningJobsByDefId.TryAdd(job.JobDefinitionId, job)) return false;
        if (!_runningJobsByCommandIdLookup.TryAdd(startedBy, job)) return false;

        if (!_chOut.Writer.TryWrite(() => RunningJobStarted?.Invoke(this, job))) return false;
        return true;

    }

    private bool RemoveRunningJob(RunningJob job)
    {
        if (!_runningJobsByDefId.TryRemove(job.JobDefinitionId, out var _)) return false;
        _chOut.Writer.TryWrite(() => RunningJobCompleted?.Invoke(this, job));
        return true;

    }

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
        _chIn.Writer.TryWrite(async () =>
        {
            await action();
            m.Release();
        });
        await m.WaitAsync(token);
    }

    public Task StartAsync(CancellationToken stoppingToken)
    {
        _chIn.Writer.TryWrite(async () => await OnStartup());
        _ = Task.Factory.StartNew(OnInput, stoppingToken, stoppingToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        _ = Task.Factory.StartNew(OnOutput, stoppingToken, stoppingToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        return Task.CompletedTask;
    }
   

    private async Task OnOutput(object? token)
    {
        CancellationToken stoppingToken = (CancellationToken)token!;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var action = await _chOut.Reader.ReadAsync(stoppingToken);
                action();
            }
        }
        catch (OperationCanceledException ex)
        {
            // do nothing.
        }
    }

    private async Task OnInput(object? token)
    {
        CancellationToken stoppingToken = (CancellationToken)token!;
        await Task.Delay(TimeSpan.FromSeconds(Debugger.IsAttached ? 5: 15), stoppingToken);
        model.JobSchduleChanged += OnJobScheduleChanged;
        model.JobAvailabilityChanged += OnJobAvailabilityChanged;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var action = await _chIn.Reader.ReadAsync(stoppingToken);
                await action();
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
            await Schedule(item.JobDefinitionId, ScheduleTrigger.Engine);
        }
        await DispatchPendingItems();
    }
    private void OnJobScheduleChanged(object? sender, JobDefinition e)
    {
        _chIn.Writer.TryWrite(async () =>
        {
            RemoveFromSchedule(new ScheduledJob(e.JobDefinitionId, DateTime.Today), _scheduledJobs);
            await Schedule(e.JobDefinitionId, ScheduleTrigger.Engine);
            await CancelWakeUp();
            await DispatchPendingItems();
        });
    }

    private void OnJobAvailabilityChanged(object? sender, JobDefinition e)
    {
        if (e.IsEnabled)
            _chIn.Writer.TryWrite(async () =>
            {
                await CancelWakeUp();
                await Schedule(e.JobDefinitionId, ScheduleTrigger.Engine);
                await DispatchPendingItems();
            });
        else
        {
            _chIn.Writer.TryWrite(async () =>
            {
                _scheduledJobs.Remove(new ScheduledJob(e.JobDefinitionId, DateTime.Today));
                await CancelWakeUp();
                await DispatchPendingItems();
            });
        }
    }

    private async Task Given(Metadata m, JobExecutionStarted ev) => _chIn.Writer.TryWrite(async () => { });


    private async Task Given(Metadata m, JobRunOnceEnqued ev)
    {
        _chIn.Writer.TryWrite(async () =>
        {
            _manualQueue.Add(new ScheduledJob(ev.JobDefinitionId, DateTime.Now));
        });
    }
    private async Task Given(Metadata m, StartJobExecutionExecuted ev)
    {
        var cmdId = ev.CommandId;
        _chIn.Writer.TryWrite(async () => await OnStartJobExecutionExecuted(cmdId));
    }

    private async Task OnStartJobExecutionExecuted(Guid cmdId)
    {
        if (_runningJobsByCommandIdLookup.TryRemove(cmdId, out var job))
        {
            RemoveRunningJob(job);
            if (model.TryGetValue(job.JobDefinitionId, out var jobDef) && jobDef.IsEnabled)
            {
                var nx = jobDef.Schedule.GetNextRunTime(DateTime.Now);
                _scheduledJobs.Add(new ScheduledJob(job.JobDefinitionId, nx)); // we add only once the same job. SortedSet is only by JobDefinitionId.
            }
        } else throw new InvalidOperationException("Cannot find running job.");


        await DispatchPendingItems();
    }

    private CancellationTokenSource? _cts;
    private async Task DispatchPendingItems(int h = 0)
    {
        
        var manuallyScheduled = _manualQueue.GetPending(DateTime.Now).ToImmutableArray();
        var scheduled = _scheduledJobs.GetPending(DateTime.Now).ToImmutableArray();
        log.LogInformation($"Dispatching items, manual: {manuallyScheduled.Length}, scheduled: {scheduled}");

        foreach (var item in manuallyScheduled)
        {
            if (this._runningJobsByDefId.ContainsKey(item.JobDefinitionId)) continue;
            if (!_runningJobsByDefId.ContainsKey(item.JobDefinitionId))
            {
                try
                {
                    await Dispatch(item.JobDefinitionId, item.StartAt, ScheduleTrigger.Manual);
                    RemoveFromSchedule(item, _manualQueue);
                }
                catch (Exception ex)
                {
                    RemoveFromSchedule(item, _manualQueue);
                    var app = await plumber.Get<JobDefinitionAggregate>(_scheduledJobs.Min.JobDefinitionId);
                    app.Disable(ex.Message);

                    await plumber.SaveChanges(app);
                }
            }
        }
        
        foreach (var item in scheduled)
        {
            if(this._runningJobsByDefId.ContainsKey(item.JobDefinitionId)) continue;
            try
            {
                await Dispatch(item.JobDefinitionId, item.StartAt, ScheduleTrigger.Engine);
                RemoveFromSchedule(item, _scheduledJobs);
            }
            catch (Exception ex)
            {
                RemoveFromSchedule(item, _scheduledJobs);
                var app = await plumber.Get<JobDefinitionAggregate>(_scheduledJobs.Min.JobDefinitionId);
                app.Disable(ex.Message);

                await plumber.SaveChanges(app);
            }
        }

        if (_scheduledJobs.Any())
        {
            TimeSpan d = _scheduledJobs.Min.StartAt - DateTimeOffset.Now;
            d = d.Add(TimeSpan.FromSeconds(1));
            if (d > TimeSpan.Zero)
                ConfigureNextIterationWakeup(d);
            else if (h < 10)
                this._chIn.Writer.TryWrite(async () => await DispatchPendingItems(h + 1));
            else throw new InvalidOperationException("Cannot configure next iteration wakeup.");
        }
    }

    private void ConfigureNextIterationWakeup(TimeSpan d)
    {
        log.LogInformation($"Configuring wake up in {d}.");
        var cts=_cts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(d, cts.Token);
                log.LogInformation("Wakeup.");
                _chIn.Writer.TryWrite(async () => await DispatchPendingItems());
            }
            catch (OperationCanceledException)
            {
                // do nothing.
            }
        });
    }
    /// <summary>
    /// Only Engine
    /// </summary>
    /// <param name="jobDefinitionId"></param>
    /// <param name="trigger"></param>
    /// <returns></returns>
    private async Task Schedule(Guid jobDefinitionId, ScheduleTrigger trigger)
    {
        var job = await model.GetAsync(jobDefinitionId);
        var at = job.Schedule.GetNextRunTime(DateTime.Now);
        AddScheduled(new ScheduledJob(jobDefinitionId, at), trigger == ScheduleTrigger.Engine ? _scheduledJobs: _manualQueue);
    }
    private async Task<Guid?> Dispatch(Guid jobDefinitionId, DateTime at, ScheduleTrigger trigger)
    {
        if (!model.TryGetValue(jobDefinitionId, out var job))
            throw new InvalidOperationException($"Job definition not found: {jobDefinitionId}");

        return await Dispatch(job, at, trigger);
    }

    private async Task<Guid?> Dispatch(JobDefinition job, DateTime at, ScheduleTrigger trigger)
    {
        var obj = JsonSerializer.Deserialize(job.Command, job.CommandType) ?? throw new InvalidOperationException($"Cannot deserialize command {job.CommandType.FullName}.");
        var cmdId = IdDuckTyping.Instance.TryGetGuidId(obj, out var commandId) ? commandId : throw new InvalidOperationException("No id on command");

        JobId id = new JobId(job.JobDefinitionId, at);
        
        using var scope = sp.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<ICommandBus>();

        var cmd = new StartJobExecution()
        {
            Command = JsonSerializer.SerializeToElement(obj),
            CommandType = obj.GetType().AssemblyQualifiedName!,
            Recipient = job.Recipient
        };

        RunningJob rj = new RunningJob(job.JobDefinitionId, id, DateTimeOffset.Now, obj.GetType().AssemblyQualifiedName!, job.Command, trigger);
        if (!AddRunningJob(rj, cmd.Id))
            throw new InvalidOperationException("Cannot dispatch more jobs.");

        await bus.QueueAsync(id, cmd, fireAndForget: true);
        log.LogInformation($"Job send for execution: {obj.GetType().Name}");

        return cmdId;
    }
}

