namespace MicroPlumberd.Services.Identity.Aggregates;

/// <summary>
/// Event raised when an identity user is deleted from the system.
/// </summary>
[OutputStream("Identity")]
public record IdentityUserDeleted
{
    /// <summary>
    /// Gets the unique identifier for the event.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();
}