namespace MicroPlumberd.Services.Identity.Aggregates;

/// <summary>
/// Event raised when a token aggregate is deleted.
/// </summary>
[OutputStream("Token")]
public record TokenAggregateDeleted
{
    /// <summary>
    /// Gets the unique identifier for the event.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();
}