namespace MicroPlumberd.Services.Identity.Aggregates;

/// <summary>
/// Event raised when a user's password is changed.
/// </summary>
[OutputStream("Identity")]
public record PasswordChanged
{
    /// <summary>
    /// Gets the unique identifier for the event.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Gets the new hashed password.
    /// </summary>
    public string PasswordHash { get; init; }


}