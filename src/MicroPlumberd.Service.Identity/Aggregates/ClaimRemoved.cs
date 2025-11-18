namespace MicroPlumberd.Services.Identity.Aggregates;

/// <summary>
/// Event raised when a claim is removed from a user's authorization profile.
/// </summary>
[OutputStream("Authorization")]
public record ClaimRemoved
{
    /// <summary>
    /// Gets the unique identifier for the event.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Gets the type of the claim being removed.
    /// </summary>
    public ClaimType ClaimType { get; init; }

    /// <summary>
    /// Gets the value of the claim being removed.
    /// </summary>
    public ClaimValue ClaimValue { get; init; }

}