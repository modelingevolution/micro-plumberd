namespace MicroPlumberd.Services.Identity.Aggregates;

/// <summary>
/// Event raised when a user's two-factor authenticator key is changed.
/// </summary>
[OutputStream("Identity")]
public record AuthenticatorKeyChanged
{
    /// <summary>
    /// Gets the unique identifier for the event.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Gets the new authenticator key for two-factor authentication.
    /// </summary>
    public string AuthenticatorKey { get; init; }

}