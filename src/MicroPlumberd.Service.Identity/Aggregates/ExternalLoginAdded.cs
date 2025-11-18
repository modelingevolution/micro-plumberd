namespace MicroPlumberd.Services.Identity.Aggregates;

/// <summary>
/// Event raised when an external login provider is added to a user's account.
/// </summary>
[OutputStream("ExternalLogin")]
public record ExternalLoginAdded
{
    /// <summary>
    /// Gets the unique identifier for the event.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Gets the external login provider.
    /// </summary>
    public ExternalLoginProvider Provider { get; init; }

    /// <summary>
    /// Gets the provider-specific key that identifies the user.
    /// </summary>
    public ExternalLoginKey ProviderKey { get; init; }

    /// <summary>
    /// Gets the display name for the external login.
    /// </summary>
    public string DisplayName { get; init; }

}