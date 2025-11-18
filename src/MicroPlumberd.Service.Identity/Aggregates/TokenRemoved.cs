namespace MicroPlumberd.Services.Identity.Aggregates;

/// <summary>
/// Event raised when a token is removed from a user's account.
/// </summary>
[OutputStream("Token")]
public record TokenRemoved
{
    /// <summary>
    /// Gets the unique identifier for the event.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Gets the name of the token being removed.
    /// </summary>
    public TokenName Name { get; init; }

    /// <summary>
    /// Gets the login provider associated with the token.
    /// </summary>
    public string LoginProvider { get; init; }

}