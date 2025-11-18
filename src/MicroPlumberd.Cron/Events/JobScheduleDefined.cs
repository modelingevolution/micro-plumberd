namespace MicroPlumberd.Services.Cron;

/// <summary>
/// Event indicating that a job's schedule has been defined.
/// </summary>
[OutputStream("JobDefinition")]
public record JobScheduleDefined
{
    /// <summary>
    /// Gets or sets the unique identifier for this event.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Gets or sets the schedule configuration for the job.
    /// </summary>
    public Schedule Schedule { get; init; }
}