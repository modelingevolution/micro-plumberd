namespace MicroPlumberd.Services.Cron;

/// <summary>
/// Command to cancel a running job execution.
/// </summary>
public record CancelJobExecution
{
    /// <summary>
    /// Gets or sets the unique identifier for this command.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();
}