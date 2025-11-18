namespace MicroPlumberd.Services.Identity.Aggregates;

/// <summary>
/// Event raised when a role is removed from a user's authorization profile.
/// </summary>
[OutputStream("Authorization")]
public record RoleRemoved
{
    /// <summary>
    /// Gets the unique identifier for the event.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Gets the identifier of the role being removed.
    /// </summary>
    public RoleIdentifier RoleId { get; init; }

}