namespace MicroPlumberd.Services.Cron;

/// <summary>
/// Event indicating that a job execution has failed.
/// </summary>
public record JobExecutionFailed
{
    /// <summary>
    /// Gets or sets the unique identifier for this event.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Gets or sets the error message describing why the job failed.
    /// </summary>
    public string Error { get; init; }
}