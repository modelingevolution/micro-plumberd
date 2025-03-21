using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Humanizer;

namespace MicroPlumberd.Services.Cron;

public interface IJobsScheduler
{
    event EventHandler Started;
    event EventHandler Stopped;
    event EventHandler<ScheduledJob>? JobScheduled;
    event EventHandler<ScheduledJob>? JobRemovedFromSchedule;
    event EventHandler<RunningJob>? RunningJobStarted;
    event EventHandler<RunningJob>? RunningJobCompleted;

    Task<JobItem<ScheduledJob>[]> ScheduledItems(CancellationToken token = default);
    Task<JobItem<RunningJob>[]> RunningItems(CancellationToken token = default);
}

public interface IJobsMonitor : IJobsScheduler
{

    event EventHandler Changed;
    IItem? GetById(Guid id);
    IReadOnlyList<IItem> Items { get; }
    ulong Scheduled { get; }
    ulong ScheduledTotal { get; }
    ulong Running { get; }
    ulong Executed { get; }
    ulong Enqueued { get;  }
}
public interface IItem : INotifyPropertyChanged
{
    JobDefinition Definition { get; init; }
    string Name { get; }
    string CommandType { get; }
    string CommandPayload { get; }
    DateTime? NextRunAt { get; }
    DateTimeOffset? Started { get; }
    TimeSpan? NextRunIn { get; }
    string Status { get; }
    bool IsRunning { get; set; }
    bool IsEnabled { get; }
}
public class JobsMonitor : IJobsMonitor, INotifyPropertyChanged, IDisposable
{
    private readonly List<Item> _items = new();
    private readonly ConcurrentDictionary<Guid, Item> _index = new();
    private bool _initialized = false;
    public event EventHandler? Changed;
    public IReadOnlyList<IItem> Items => _items;
    public IItem? GetById(Guid id) => _index.TryGetValue(id, out var r) ? r : null;

    

    public record Item : IItem, IEquatable<Item>
    {
        private DateTime? _nextRunAt;
        private DateTimeOffset? _started;
        public required JobDefinition Definition { get; init; }
        public string Name => Definition.Name;
        public string CommandType => Definition.CommandType.Name.Humanize();
        public string CommandPayload => Definition.Command.ToString();

        public DateTime? NextRunAt
        {
            get => _nextRunAt;
            internal set
            {
                if (SetField(ref _nextRunAt, value)) 
                    OnPropertyChanged(nameof(NextRunIn));
            }
        }

        public DateTimeOffset? Started
        {
            get => _started;
            internal set
            {
                if (SetField(ref _started, value)) 
                    OnPropertyChanged(nameof(Status));
            }
        }

        public TimeSpan? NextRunIn => (NextRunAt.HasValue && !IsRunning) ? (NextRunAt - DateTime.Now) : null;

        public string Status
        {
            get
            {
                if (IsRunning)
                {
                    if (Started.HasValue)
                        return $"Running for {(DateTime.Now - Started.Value).Humanize()}";
                    else return "Running, unknown.";
                } 
                else if (IsEnabled)
                {
                    return NextRunIn.HasValue ? $"Next run in {NextRunIn.Value.Humanize()}" : "Calculating...";
                }
                else return "Disabled";
            }
        }
        public bool IsRunning { get; set; }
        public bool IsEnabled => Definition.IsEnabled;

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
    }
    private readonly IJobsScheduler _scheduler;
    private readonly ILogger<JobsMonitor> _log;
    private readonly JobDefinitionModel _model;
    private ulong _scheduled;
    private ulong _executed;
    private ulong _scheduledTotal;
    private ulong _running;
    private ulong _enqueued;

    public ulong Enqueued
    {
        get => _enqueued;
        private set => SetField(ref _enqueued, value);
    }

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

    public JobsMonitor(IJobsScheduler scheduler, ILogger<JobsMonitor> log, JobDefinitionModel model)
    {
        _scheduler = scheduler;
        _log = log;
        _model = model;
        _model.Added += OnJobAdded;
        _model.Removed += OnJobRemoved;
        scheduler.JobScheduled += OnSchedulerOnJobScheduled;
        scheduler.JobRemovedFromSchedule += OnSchedulerOnJobRemovedFromSchedule;
        scheduler.RunningJobStarted += OnSchedulerOnRunningJobStarted;
        scheduler.RunningJobCompleted += OnSchedulerOnRunningJobCompleted;
    }

    private void OnJobRemoved(object? sender, JobDefinition item)
    {
        if (_index.TryRemove(item.JobDefinitionId, out var record))
            _items.Remove(record);
    }

    private void OnJobAdded(object? sender, JobDefinition item)
    {
        var record = new Item
        {
            Definition = item
        };
        if (_index.TryAdd(item.JobDefinitionId, record))
            _items.Add(record);
       
    }

    private void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;
        foreach (var item in _model.JobDefinitions)
        {
            var record = new Item
            {
                Definition = item
            };

            if(_index.TryAdd(item.JobDefinitionId, record))
                _items.Add(record);
            
        }
    }
    private void OnSchedulerOnRunningJobCompleted(object? s, RunningJob e)
    {
        EnsureInitialized();
        _log.LogInformation($"Job completed: {e.CommandType}");
        Running--;
        Executed++;
        if (_index.TryGetValue(e.JobDefinitionId, out var r)) r.IsRunning = false;
        Changed?.Invoke(this, EventArgs.Empty);

    }

    private void OnSchedulerOnRunningJobStarted(object? s, RunningJob e)
    {
        EnsureInitialized();
        _log.LogInformation($"Job started: {e.CommandType}");
        Running++;
        if (_index.TryGetValue(e.JobDefinitionId, out var r))
        {
            r.Started = e.Created;
            r.IsRunning = true;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnSchedulerOnJobRemovedFromSchedule(object? s, ScheduledJob e)
    {
        EnsureInitialized();
        _log.LogInformation($"Job remove from schedule: {e.JobDefinitionId}");
        if (e.Trigger == ScheduleTrigger.Engine)
        {
            Scheduled--;
        }
        Enqueued--;

        if (_index.TryGetValue(e.JobDefinitionId, out var r)) r.NextRunAt = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnSchedulerOnJobScheduled(object? s, ScheduledJob e)
    {
        EnsureInitialized();
        _log.LogInformation($"Job scheduled: {e.JobDefinitionId} in {e.StartAt - DateTime.Now}");
        if (e.Trigger == ScheduleTrigger.Engine)
        {
            Scheduled++;
            ScheduledTotal++;
        }
        Enqueued++;
        
        if (_index.TryGetValue(e.JobDefinitionId, out var r)) r.NextRunAt = e.StartAt;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? Started
    {
        add => _scheduler.Started += value;
        remove => _scheduler.Started -= value;
    }

    public event EventHandler? Stopped
    {
        add => _scheduler.Stopped += value;
        remove => _scheduler.Stopped -= value;
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
        _model.Added -= OnJobAdded;
        _model.Removed -= OnJobRemoved;
    }
}