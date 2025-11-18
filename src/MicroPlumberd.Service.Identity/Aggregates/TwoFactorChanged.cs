namespace MicroPlumberd.Services.Identity.Aggregates;

/// <summary>
/// Event raised when two-factor authentication is enabled or disabled for a user.
/// </summary>
[OutputStream("Identity")]
public record TwoFactorChanged
{
    /// <summary>
    /// Gets the unique identifier for the event.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Gets a value indicating whether two-factor authentication is enabled.
    /// </summary>
    public bool TwoFactorEnabled { get; init; }

}