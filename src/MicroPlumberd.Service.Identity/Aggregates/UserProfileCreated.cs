namespace MicroPlumberd.Services.Identity.Aggregates;

/// <summary>
/// Event raised when a new user profile is created.
/// </summary>
[OutputStream("UserProfile")]
public record UserProfileCreated
{
    /// <summary>
    /// Gets the unique identifier for the user profile.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Gets the username.
    /// </summary>
    public string UserName { get; init; }

    /// <summary>
    /// Gets the normalized username for case-insensitive comparisons.
    /// </summary>
    public string NormalizedUserName { get; init; }

    /// <summary>
    /// Gets the email address.
    /// </summary>
    public string Email { get; init; }

    /// <summary>
    /// Gets the normalized email address for case-insensitive comparisons.
    /// </summary>
    public string NormalizedEmail { get; init; }

    /// <summary>
    /// Gets the phone number.
    /// </summary>
    public string PhoneNumber { get; init; }

}