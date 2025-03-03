namespace MicroPlumberd.Services.Identity.Aggregates;

[OutputStream("ExternalLogin")]
public record ExternalLoginAggregateDeleted
{
    public Guid Id { get; init; } = Guid.NewGuid();
}