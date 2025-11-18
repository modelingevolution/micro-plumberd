namespace MicroPlumberd.Services.Identity.Aggregates;

/// <summary>
/// Event raised when an external login aggregate is deleted.
/// </summary>
[OutputStream("ExternalLogin")]
public record ExternalLoginAggregateDeleted
{
    /// <summary>
    /// Gets the unique identifier for the event.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();
}