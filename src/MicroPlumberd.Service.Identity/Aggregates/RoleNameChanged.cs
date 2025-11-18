namespace MicroPlumberd.Services.Identity.Aggregates;

/// <summary>
/// Event raised when a role's name is changed.
/// </summary>
[OutputStream("Role")]
public record RoleNameChanged
{
    /// <summary>
    /// Gets the unique identifier for the event.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Gets the new name of the role.
    /// </summary>
    public string Name { get; init; }

    /// <summary>
    /// Gets the normalized name of the role for case-insensitive comparisons.
    /// </summary>
    public string NormalizedName { get; init; }

}