using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MicroPlumberd.Services.Cron;

/// <summary>
/// Represents a job definition with its configuration and state.
/// </summary>
public record JobDefinition : INotifyPropertyChanged
{
    private string _name;
    private bool _isEnabled;
    private Type _commandType;
    private string _recipient;
    private JsonElement _command;
    private Schedule _schedule;

    /// <summary>
    /// Gets the unique identifier for this job definition.
    /// </summary>
    public required Guid JobDefinitionId { get; init; }

    /// <summary>
    /// Gets or sets the schedule configuration for the job.
    /// </summary>
    public Schedule Schedule
    {
        get => _schedule;
        set => SetField(ref _schedule, value);
    }

    /// <summary>
    /// Gets or sets the command payload as a JSON element.
    /// </summary>
    public JsonElement Command
    {
        get => _command;
        set => SetField(ref _command, value);
    }

    /// <summary>
    /// Gets or sets the recipient identifier that will handle the command.
    /// </summary>
    public string Recipient
    {
        get => _recipient;
        set => SetField(ref _recipient, value);
    }

    /// <summary>
    /// Gets or sets the type of command to execute.
    /// </summary>
    public Type CommandType
    {
        get => _commandType;
        set => SetField(ref _commandType, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the job is enabled for execution.
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetField(ref _isEnabled, value);
    }

    /// <summary>
    /// Gets or sets the name of the job.
    /// </summary>
    public required string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the job has been deleted.
    /// </summary>
    public bool IsDeleted { get; set; }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Raises the <see cref="PropertyChanged"/> event.
    /// </summary>
    /// <param name="propertyName">The name of the property that changed.</param>
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// Sets a field value and raises the <see cref="PropertyChanged"/> event if the value changed.
    /// </summary>
    /// <typeparam name="T">The type of the field.</typeparam>
    /// <param name="field">A reference to the field to set.</param>
    /// <param name="value">The new value for the field.</param>
    /// <param name="propertyName">The name of the property being set.</param>
    /// <returns>True if the value changed; otherwise, false.</returns>
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

/// <summary>
/// Maintains a read model of all job definitions by handling job-related events.
/// </summary>
[EventHandler]
public partial class JobDefinitionModel
{
    /// <summary>
    /// Occurs when a job's schedule has changed.
    /// </summary>
    public event EventHandler<JobDefinition> JobSchduleChanged;

    /// <summary>
    /// Occurs when a job's availability (enabled/disabled state) has changed.
    /// </summary>
    public event EventHandler<JobDefinition> JobAvailabilityChanged;


    private readonly ConcurrentDictionary<Guid, JobDefinition> _jobDefinitions = new();

    /// <summary>
    /// Occurs when a new job definition has been added to the model.
    /// </summary>
    public event EventHandler<JobDefinition> Added;

    /// <summary>
    /// Occurs when a job definition has been removed from the model.
    /// </summary>
    public event EventHandler<JobDefinition> Removed;

    /// <summary>
    /// Tries to retrieve a job definition by its identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the job definition.</param>
    /// <param name="job">The job definition if found.</param>
    /// <returns>True if the job was found; otherwise, false.</returns>
    public bool TryGetValue(Guid id, out JobDefinition job) => _jobDefinitions.TryGetValue(id, out job);
    private async Task Given(Metadata m, JobEnabled ev)
    {
        var job = this[m.StreamId<Guid>()];
        job.IsEnabled = true;
        this.JobAvailabilityChanged?.Invoke(this, job);
    }
    /// <summary>
    /// Gets an immutable array of all job definitions.
    /// </summary>
    public ImmutableArray<JobDefinition> JobDefinitions => [.._jobDefinitions.Values];

    private async Task Given(Metadata m, JobDisabled ev)
    {
        var job = this[m.StreamId<Guid>()];
        job.IsEnabled = false;
        this.JobAvailabilityChanged?.Invoke(this, job);
    }
    private async Task Given(Metadata m, JobDeleted ev)
    {
        bool removePermanently = JobSchduleChanged == null! && JobAvailabilityChanged == null! && Removed == null!;
        if(removePermanently)
            _jobDefinitions.TryRemove(m.StreamId<Guid>(), out _);
        else if (_jobDefinitions.TryGetValue(m.StreamId<Guid>(), out var x))
        {
            x.IsDeleted = true;
            Removed?.Invoke(this,x);
        }
    }
    private async Task Given(Metadata m, JobNamed ev)
    {
        var jobDefinitionId = m.StreamId<Guid>();
        bool added = false;


        var job = _jobDefinitions.GetOrAdd(jobDefinitionId, x =>
            {
                var jobDefinition = new JobDefinition()
                {
                    JobDefinitionId = jobDefinitionId,
                    Schedule = new EmptySchedule(),
                    Name = ev.Name
                };
                added = true;
                return jobDefinition;
            });
        job.Name = ev.Name;

        if (added)
        {
            Trace.Assert(m.SourceStreamPosition == 0);
            Added?.Invoke(this, job);
        }
        else
            Trace.Assert(m.SourceStreamPosition > 0);
    }

    /// <summary>
    /// Asynchronously retrieves a job definition by its identifier, waiting if necessary.
    /// </summary>
    /// <param name="id">The unique identifier of the job definition.</param>
    /// <returns>A task representing the asynchronous operation, containing the job definition.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the job is not found after multiple retry attempts.</exception>
    public async ValueTask<JobDefinition> GetAsync(Guid id)
    {
        int c = 0;
        JobDefinition job;
        while (!TryGetValue(id, out job))
        {
            await Task.Delay(200);
            c += 1;
            if(c > 10) throw new InvalidOperationException("Job not found");
        }

        return job;
    }

    /// <summary>
    /// Gets a job definition by its identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the job definition.</param>
    /// <returns>The job definition.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the job is not found.</exception>
    public JobDefinition this[Guid id]
    {
        get => TryGetValue(id, out var job) ? job : throw new ArgumentOutOfRangeException("Job not found");
    }
    private async Task Given(Metadata m, JobScheduleDefined ev) => this[m.StreamId<Guid>()].Schedule = ev.Schedule;

    private async Task Given(Metadata m, JobProcessDefined ev)
    {
        var jobDef = this[m.StreamId<Guid>()];
        jobDef.CommandType = Type.GetType(ev.CommandType)!;
        jobDef.Recipient = ev.Recipient;
        jobDef.Command = ev.CommandPayload;
        JobSchduleChanged?.Invoke(this, jobDef);
    }

    
}