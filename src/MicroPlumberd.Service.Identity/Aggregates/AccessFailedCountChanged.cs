namespace MicroPlumberd.Services.Identity.Aggregates;

/// <summary>
/// Event raised when the number of failed access attempts for a user is changed.
/// </summary>
[OutputStream("Identity")]
public record AccessFailedCountChanged
{
    /// <summary>
    /// Gets the unique identifier for the event.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Gets the new count of failed access attempts.
    /// </summary>
    public int AccessFailedCount { get; init; }

}