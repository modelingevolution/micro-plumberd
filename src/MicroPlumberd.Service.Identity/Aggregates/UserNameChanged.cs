namespace MicroPlumberd.Services.Identity.Aggregates;

/// <summary>
/// Event raised when a user's username is changed.
/// </summary>
[OutputStream("UserProfile")]
public record UserNameChanged
{
    /// <summary>
    /// Gets the unique identifier for the event.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Gets the new username.
    /// </summary>
    public string UserName { get; init; }

    /// <summary>
    /// Gets the normalized username for case-insensitive comparisons.
    /// </summary>
    public string NormalizedUserName { get; init; }

}