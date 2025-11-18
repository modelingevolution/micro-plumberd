namespace MicroPlumberd.Services.Identity.Aggregates;

/// <summary>
/// Event raised when the lockout enabled status for a user is changed.
/// </summary>
[OutputStream("Identity")]
public record LockoutEnabledChanged
{
    /// <summary>
    /// Gets the unique identifier for the event.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Gets a value indicating whether lockout is enabled for the user.
    /// </summary>
    public bool LockoutEnabled { get; init; }

}