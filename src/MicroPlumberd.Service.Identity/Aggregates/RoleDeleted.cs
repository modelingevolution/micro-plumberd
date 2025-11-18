namespace MicroPlumberd.Services.Identity.Aggregates;

/// <summary>
/// Event raised when a role is deleted from the identity system.
/// </summary>
[OutputStream("Role")]
public record RoleDeleted
{
    /// <summary>
    /// Gets the unique identifier for the event.
    /// </summary>
    public Guid Id { get; init; }= Guid.NewGuid();
}