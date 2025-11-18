namespace MicroPlumberd.Services.Identity.Aggregates;

/// <summary>
/// Event raised when a user's email address is changed.
/// </summary>
[OutputStream("UserProfile")]
public record EmailChanged
{
    /// <summary>
    /// Gets the unique identifier for the event.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Gets the new email address.
    /// </summary>
    public string Email { get; init; }

    /// <summary>
    /// Gets the normalized email address for case-insensitive comparisons.
    /// </summary>
    public string NormalizedEmail { get; init; }

}