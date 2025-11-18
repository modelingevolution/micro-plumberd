namespace MicroPlumberd.Services.Identity.Aggregates;

/// <summary>
/// Event raised when a claim is added to a user's authorization profile.
/// </summary>
[OutputStream("Authorization")]
public record ClaimAdded
{
    /// <summary>
    /// Gets the unique identifier for the event.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Gets the type of the claim being added.
    /// </summary>
    public ClaimType ClaimType { get; init; }

    /// <summary>
    /// Gets the value of the claim being added.
    /// </summary>
    public ClaimValue ClaimValue { get; init; }

}