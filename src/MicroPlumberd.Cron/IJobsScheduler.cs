using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Humanizer;

namespace MicroPlumberd.Services.Cron;

/// <summary>
/// Provides events and methods for monitoring job scheduling and execution.
/// </summary>
public interface IJobsScheduler
{
    /// <summary>
    /// Occurs when the job scheduler has started.
    /// </summary>
    event EventHandler Started;

    /// <summary>
    /// Occurs when the job scheduler has stopped.
    /// </summary>
    event EventHandler Stopped;

    /// <summary>
    /// Occurs when a job has been scheduled for execution.
    /// </summary>
    event EventHandler<ScheduledJob>? JobScheduled;

    /// <summary>
    /// Occurs when a job has been removed from the schedule.
    /// </summary>
    event EventHandler<ScheduledJob>? JobRemovedFromSchedule;

    /// <summary>
    /// Occurs when a job has started running.
    /// </summary>
    event EventHandler<RunningJob>? RunningJobStarted;

    /// <summary>
    /// Occurs when a running job has completed.
    /// </summary>
    event EventHandler<RunningJob>? RunningJobCompleted;

    /// <summary>
    /// Gets all currently scheduled jobs.
    /// </summary>
    /// <param name="token">A cancellation token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation, containing an array of scheduled job items.</returns>
    Task<JobItem<ScheduledJob>[]> ScheduledItems(CancellationToken token = default);

    /// <summary>
    /// Gets all currently running jobs.
    /// </summary>
    /// <param name="token">A cancellation token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation, containing an array of running job items.</returns>
    Task<JobItem<RunningJob>[]> RunningItems(CancellationToken token = default);
}

/// <summary>
/// Extends the job scheduler interface with monitoring capabilities and statistics.
/// </summary>
public interface IJobsMonitor : IJobsScheduler
{
    /// <summary>
    /// Occurs when any monitored job state has changed.
    /// </summary>
    event EventHandler Changed;

    /// <summary>
    /// Retrieves a job item by its unique identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the job.</param>
    /// <returns>The job item if found; otherwise, null.</returns>
    IItem? GetById(Guid id);

    /// <summary>
    /// Gets a read-only list of all monitored job items.
    /// </summary>
    IReadOnlyList<IItem> Items { get; }

    /// <summary>
    /// Gets the number of jobs currently scheduled by the scheduler engine.
    /// </summary>
    ulong Scheduled { get; }

    /// <summary>
    /// Gets the total number of jobs that have been scheduled since the monitor started.
    /// </summary>
    ulong ScheduledTotal { get; }

    /// <summary>
    /// Gets the number of jobs currently running.
    /// </summary>
    ulong Running { get; }

    /// <summary>
    /// Gets the total number of jobs that have been executed.
    /// </summary>
    ulong Executed { get; }

    /// <summary>
    /// Gets the total number of jobs currently enqueued (scheduled or manual).
    /// </summary>
    ulong Enqueued { get;  }
}

/// <summary>
/// Represents a monitored job item with its definition and current state.
/// </summary>
public interface IItem : INotifyPropertyChanged
{
    /// <summary>
    /// Gets the job definition associated with this item.
    /// </summary>
    JobDefinition Definition { get; init; }

    /// <summary>
    /// Gets the name of the job.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the command type name in a human-readable format.
    /// </summary>
    string CommandType { get; }

    /// <summary>
    /// Gets the serialized command payload.
    /// </summary>
    string CommandPayload { get; }

    /// <summary>
    /// Gets the next scheduled run time, if available.
    /// </summary>
    DateTime? NextRunAt { get; }

    /// <summary>
    /// Gets the time when the job started running, if currently running.
    /// </summary>
    DateTimeOffset? Started { get; }

    /// <summary>
    /// Gets the time remaining until the next run, if scheduled.
    /// </summary>
    TimeSpan? NextRunIn { get; }

    /// <summary>
    /// Gets a human-readable status description of the job.
    /// </summary>
    string Status { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the job is currently running.
    /// </summary>
    bool IsRunning { get; set; }

    /// <summary>
    /// Gets a value indicating whether the job is enabled for execution.
    /// </summary>
    bool IsEnabled { get; }
}
/// <summary>
/// Monitors job definitions and their execution state, providing real-time statistics and notifications.
/// </summary>
public class JobsMonitor : IJobsMonitor, INotifyPropertyChanged, IDisposable
{
    private readonly List<Item> _items = new();
    private readonly ConcurrentDictionary<Guid, Item> _index = new();
    private bool _initialized = false;

    /// <inheritdoc/>
    public event EventHandler? Changed;

    /// <inheritdoc/>
    public IReadOnlyList<IItem> Items => _items;

    /// <inheritdoc/>
    public IItem? GetById(Guid id) => _index.TryGetValue(id, out var r) ? r : null;

    

    /// <summary>
    /// Represents an item in the job monitor with observable properties for tracking job state.
    /// </summary>
    public record Item : IItem, IEquatable<Item>
    {
        private DateTime? _nextRunAt;
        private DateTimeOffset? _started;

        /// <inheritdoc/>
        public required JobDefinition Definition { get; init; }

        /// <inheritdoc/>
        public string Name => Definition.Name;

        /// <inheritdoc/>
        public string CommandType => Definition.CommandType.Name.Humanize();

        /// <inheritdoc/>
        public string CommandPayload => Definition.Command.ToString();

        /// <inheritdoc/>
        public DateTime? NextRunAt
        {
            get => _nextRunAt;
            internal set
            {
                if (SetField(ref _nextRunAt, value))
                    OnPropertyChanged(nameof(NextRunIn));
            }
        }

        /// <inheritdoc/>
        public DateTimeOffset? Started
        {
            get => _started;
            internal set
            {
                if (SetField(ref _started, value))
                    OnPropertyChanged(nameof(Status));
            }
        }

        /// <inheritdoc/>
        public TimeSpan? NextRunIn => (NextRunAt.HasValue && !IsRunning) ? (NextRunAt - DateTime.Now) : null;

        /// <inheritdoc/>
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

        /// <inheritdoc/>
        public bool IsRunning { get; set; }

        /// <inheritdoc/>
        public bool IsEnabled => Definition.IsEnabled;

        /// <inheritdoc/>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Raises the <see cref="PropertyChanged"/> event to notify listeners of a property value change.
        /// </summary>
        /// <param name="propertyName">The name of the property that changed. Automatically captured from the calling member when not specified.</param>
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Updates a field with a new value if it differs from the current value and raises the <see cref="PropertyChanged"/> event.
        /// </summary>
        /// <typeparam name="T">The type of the field being updated.</typeparam>
        /// <param name="field">A reference to the field to update.</param>
        /// <param name="value">The new value to assign to the field.</param>
        /// <param name="propertyName">The name of the property being changed. Automatically captured from the calling member when not specified.</param>
        /// <returns>True if the field value was changed; otherwise, false.</returns>
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

    /// <inheritdoc/>
    public ulong Enqueued
    {
        get => _enqueued;
        private set => SetField(ref _enqueued, value);
    }

    /// <inheritdoc/>
    public ulong Scheduled
    {
        get => _scheduled;
        private set => SetField(ref _scheduled, value);
    }

    /// <inheritdoc/>
    public ulong ScheduledTotal
    {
        get => _scheduledTotal;
        private set => SetField(ref _scheduledTotal, value);
    }

    /// <inheritdoc/>
    public ulong Running
    {
        get => _running;
        private set => SetField(ref _running, value);
    }

    /// <inheritdoc/>
    public ulong Executed
    {
        get => _executed;
        private set => SetField(ref _executed, value);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JobsMonitor"/> class.
    /// </summary>
    /// <param name="scheduler">The job scheduler to monitor.</param>
    /// <param name="log">The logger instance.</param>
    /// <param name="model">The job definition model containing job metadata.</param>
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

    /// <inheritdoc/>
    public event EventHandler? Started
    {
        add => _scheduler.Started += value;
        remove => _scheduler.Started -= value;
    }

    /// <inheritdoc/>
    public event EventHandler? Stopped
    {
        add => _scheduler.Stopped += value;
        remove => _scheduler.Stopped -= value;
    }

    /// <inheritdoc/>
    public event EventHandler<ScheduledJob>? JobScheduled
    {
        add => _scheduler.JobScheduled += value;
        remove => _scheduler.JobScheduled -= value;
    }

    /// <inheritdoc/>
    public event EventHandler<ScheduledJob>? JobRemovedFromSchedule
    {
        add => _scheduler.JobRemovedFromSchedule += value;
        remove => _scheduler.JobRemovedFromSchedule -= value;
    }

    /// <inheritdoc/>
    public event EventHandler<RunningJob>? RunningJobStarted
    {
        add => _scheduler.RunningJobStarted += value;
        remove => _scheduler.RunningJobStarted -= value;
    }

    /// <inheritdoc/>
    public event EventHandler<RunningJob>? RunningJobCompleted
    {
        add => _scheduler.RunningJobCompleted += value;
        remove => _scheduler.RunningJobCompleted -= value;
    }

    /// <inheritdoc/>
    public Task<JobItem<ScheduledJob>[]> ScheduledItems(CancellationToken token = default)
    {
        return _scheduler.ScheduledItems(token);
    }

    /// <inheritdoc/>
    public Task<JobItem<RunningJob>[]> RunningItems(CancellationToken token = default)
    {
        return _scheduler.RunningItems(token);
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Raises the <see cref="PropertyChanged"/> event to notify listeners of a property value change.
    /// </summary>
    /// <param name="propertyName">The name of the property that changed. Automatically captured from the calling member when not specified.</param>
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// Updates a field with a new value if it differs from the current value and raises the <see cref="PropertyChanged"/> event.
    /// </summary>
    /// <typeparam name="T">The type of the field being updated.</typeparam>
    /// <param name="field">A reference to the field to update.</param>
    /// <param name="value">The new value to assign to the field.</param>
    /// <param name="propertyName">The name of the property being changed. Automatically captured from the calling member when not specified.</param>
    /// <returns>True if the field value was changed; otherwise, false.</returns>
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    /// <summary>
    /// Releases all resources used by the <see cref="JobsMonitor"/>.
    /// </summary>
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