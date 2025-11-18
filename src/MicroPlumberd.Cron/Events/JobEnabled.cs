namespace MicroPlumberd.Services.Cron;

/// <summary>
/// Event indicating that a job definition has been enabled.
/// </summary>
[OutputStream("JobDefinition")]
public record JobEnabled
{
    /// <summary>
    /// Gets or sets the unique identifier for this event.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();
}