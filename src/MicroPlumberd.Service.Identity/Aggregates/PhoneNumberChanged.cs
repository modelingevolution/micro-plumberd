namespace MicroPlumberd.Services.Identity.Aggregates;

/// <summary>
/// Event raised when a user's phone number is changed.
/// </summary>
[OutputStream("UserProfile")]
public record PhoneNumberChanged
{
    /// <summary>
    /// Gets the unique identifier for the event.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Gets the new phone number.
    /// </summary>
    public string PhoneNumber { get; init; }

}