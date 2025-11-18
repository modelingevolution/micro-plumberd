namespace MicroPlumberd.Services.Identity.Aggregates;

/// <summary>
/// Event raised when the lockout end time for a user is changed.
/// </summary>
[OutputStream("Identity")]
public record LockoutEndChanged
{
    /// <summary>
    /// Gets the unique identifier for the event.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Gets the date and time when the lockout ends. Null if lockout is not active.
    /// </summary>
    public DateTimeOffset? LockoutEnd { get; init; }

}