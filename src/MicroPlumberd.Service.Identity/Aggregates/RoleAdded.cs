namespace MicroPlumberd.Services.Identity.Aggregates;

/// <summary>
/// Event raised when a role is added to a user's authorization profile.
/// </summary>
[OutputStream("Authorization")]
public record RoleAdded
{
    /// <summary>
    /// Gets the unique identifier for the event.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Gets the identifier of the role being added.
    /// </summary>
    public RoleIdentifier RoleId { get; init; }

}