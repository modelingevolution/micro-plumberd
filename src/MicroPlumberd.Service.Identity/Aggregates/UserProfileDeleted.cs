namespace MicroPlumberd.Services.Identity.Aggregates;

/// <summary>
/// Event raised when a user profile is deleted.
/// </summary>
[OutputStream("UserProfile")]
public record UserProfileDeleted
{
    /// <summary>
    /// Gets the unique identifier for the event.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();
}