namespace MicroPlumberd.Services.Identity.Aggregates;

/// <summary>
/// Event raised when a user's phone number is confirmed.
/// </summary>
[OutputStream("UserProfile")]
public record PhoneNumberConfirmed
{
    /// <summary>
    /// Gets the unique identifier for the event.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

}