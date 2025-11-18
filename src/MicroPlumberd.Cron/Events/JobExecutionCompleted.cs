namespace MicroPlumberd.Services.Cron;

/// <summary>
/// Event indicating that a job execution has completed successfully.
/// </summary>
public record JobExecutionCompleted
{
    /// <summary>
    /// Gets or sets the unique identifier for this event.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();
}