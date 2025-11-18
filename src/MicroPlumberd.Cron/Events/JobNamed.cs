namespace MicroPlumberd.Services.Cron;

/// <summary>
/// Event indicating that a job definition has been named or renamed.
/// </summary>
[OutputStream("JobDefinition")]
public record JobNamed
{
    /// <summary>
    /// Gets or sets the unique identifier for this event.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Gets or sets the name of the job.
    /// </summary>
    public string Name { get; init; }
}

/// <summary>
/// Event indicating that a job definition has been deleted.
/// </summary>
[OutputStream("JobDefinition")]
public record JobDeleted
{
    /// <summary>
    /// Gets or sets the unique identifier for this event.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();
}