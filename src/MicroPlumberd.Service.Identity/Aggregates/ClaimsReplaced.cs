using System.Security.Claims;

namespace MicroPlumberd.Services.Identity.Aggregates;

/// <summary>
/// Event raised when all claims for a user are replaced with a new set of claims.
/// </summary>
[OutputStream("Authorization")]
public record ClaimsReplaced
{
    /// <summary>
    /// Gets the unique identifier for the event.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Gets the new set of claims that replace the existing claims.
    /// </summary>
    public List<Claim> Claims { get; init; }

}