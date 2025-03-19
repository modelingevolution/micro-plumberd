using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MicroPlumberd.Services.Cron;

public interface IJobsScheduler
{
    event EventHandler<ScheduledJob>? JobScheduled;
    event EventHandler<ScheduledJob>? JobRemovedFromSchedule;
    event EventHandler<RunningJob>? RunningJobStarted;
    event EventHandler<RunningJob>? RunningJobCompleted;

    Task<JobItem<ScheduledJob>[]> ScheduledItems(CancellationToken token = default);
    Task<JobItem<RunningJob>[]> RunningItems(CancellationToken token = default);
}

public interface IJobsMonitor : IJobsScheduler
{
    ulong Scheduled { get; }
    ulong ScheduledTotal { get; }
    ulong Running { get; }
    ulong Executed { get; }
}

public class JobsMonitor : IJobsMonitor, INotifyPropertyChanged, IDisposable
{
    private readonly IJobsScheduler _scheduler;
    private readonly ILogger<JobsMonitor> _log;
    private ulong _scheduled;
    private ulong _executed;
    private ulong _scheduledTotal;
    private ulong _running;

    public ulong Scheduled
    {
        get => _scheduled;
        private set => SetField(ref _scheduled, value);
    }

    public ulong ScheduledTotal
    {
        get => _scheduledTotal;
        private set => SetField(ref _scheduledTotal, value);
    }

    public ulong Running
    {
        get => _running;
        private set => SetField(ref _running, value);
    }
    
    public ulong Executed
    {
        get => _executed;
        private set => SetField(ref _executed, value);
    }

    public JobsMonitor(IJobsScheduler scheduler, ILogger<JobsMonitor> log)
    {
        _scheduler = scheduler;
        _log = log;
        scheduler.JobScheduled += OnSchedulerOnJobScheduled;
        scheduler.JobRemovedFromSchedule += OnSchedulerOnJobRemovedFromSchedule;
        scheduler.RunningJobStarted += OnSchedulerOnRunningJobStarted;
        scheduler.RunningJobCompleted += OnSchedulerOnRunningJobCompleted;
    }

    private void OnSchedulerOnRunningJobCompleted(object? s, RunningJob e)
    {
        _log.LogInformation($"Job completed: {e.CommandType}");
        Running--;
        Executed++;
    }

    private void OnSchedulerOnRunningJobStarted(object? s, RunningJob e)
    {
        _log.LogInformation($"Job started: {e.CommandType}");
        Running++;
    }

    private void OnSchedulerOnJobRemovedFromSchedule(object? s, ScheduledJob e)
    {
        _log.LogInformation($"Job remove from schedule: {e.JobDefinitionId}");
        Scheduled--;
    }

    private void OnSchedulerOnJobScheduled(object? s, ScheduledJob e)
    {
        _log.LogInformation($"Job scheduled: {e.JobDefinitionId} in {e.StartAt - DateTime.Now}");
        Scheduled++;
        ScheduledTotal++;
    }

    public event EventHandler<ScheduledJob>? JobScheduled
    {
        add => _scheduler.JobScheduled += value;
        remove => _scheduler.JobScheduled -= value;
    }

    public event EventHandler<ScheduledJob>? JobRemovedFromSchedule
    {
        add => _scheduler.JobRemovedFromSchedule += value;
        remove => _scheduler.JobRemovedFromSchedule -= value;
    }

    public event EventHandler<RunningJob>? RunningJobStarted
    {
        add => _scheduler.RunningJobStarted += value;
        remove => _scheduler.RunningJobStarted -= value;
    }

    public event EventHandler<RunningJob>? RunningJobCompleted
    {
        add => _scheduler.RunningJobCompleted += value;
        remove => _scheduler.RunningJobCompleted -= value;
    }

    public Task<JobItem<ScheduledJob>[]> ScheduledItems(CancellationToken token = default)
    {
        return _scheduler.ScheduledItems(token);
    }

    public Task<JobItem<RunningJob>[]> RunningItems(CancellationToken token = default)
    {
        return _scheduler.RunningItems(token);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    public void Dispose()
    {
        _scheduler.JobScheduled -= OnSchedulerOnJobScheduled;
        _scheduler.JobRemovedFromSchedule -= OnSchedulerOnJobRemovedFromSchedule;
        _scheduler.RunningJobStarted -= OnSchedulerOnRunningJobStarted;
        _scheduler.RunningJobCompleted -= OnSchedulerOnRunningJobCompleted;
    }
}