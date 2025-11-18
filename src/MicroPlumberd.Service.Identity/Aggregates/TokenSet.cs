namespace MicroPlumberd.Services.Identity.Aggregates;

/// <summary>
/// Event raised when a token is set or updated for a user.
/// </summary>
[OutputStream("Token")]
public record TokenSet
{
    /// <summary>
    /// Gets the unique identifier for the event.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Gets the name of the token.
    /// </summary>
    public TokenName Name { get; init; }

    /// <summary>
    /// Gets the value of the token.
    /// </summary>
    public TokenValue Value { get; init; }

    /// <summary>
    /// Gets the login provider associated with the token.
    /// </summary>
    public string LoginProvider { get; init; }

}