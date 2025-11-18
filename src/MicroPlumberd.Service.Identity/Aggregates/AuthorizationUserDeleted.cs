namespace MicroPlumberd.Services.Identity.Aggregates;

/// <summary>
/// Event raised when a user's authorization profile is deleted.
/// </summary>
[OutputStream("Authorization")]
public record AuthorizationUserDeleted
{
    /// <summary>
    /// Gets the unique identifier for the event.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();
}