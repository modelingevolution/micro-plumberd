namespace MicroPlumberd.Services.Identity.Aggregates;

[OutputStream("Token")]
public record TokenAggregateDeleted
{
    public Guid Id { get; init; } = Guid.NewGuid();
}