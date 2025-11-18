namespace MicroPlumberd.Services.Cron;

/// <summary>
/// Event indicating that a job definition has been disabled.
/// </summary>
[OutputStream("JobDefinition")]
public record JobDisabled
{
    /// <summary>
    /// Gets or sets the unique identifier for this event.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Gets or sets the reason why the job was disabled.
    /// </summary>
    public string Reason { get; init; }
}