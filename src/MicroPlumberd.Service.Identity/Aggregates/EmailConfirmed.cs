namespace MicroPlumberd.Services.Identity.Aggregates;

/// <summary>
/// Event raised when a user's email address is confirmed.
/// </summary>
[OutputStream("UserProfile")]
public record EmailConfirmed
{
    /// <summary>
    /// Gets the unique identifier for the event.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

}