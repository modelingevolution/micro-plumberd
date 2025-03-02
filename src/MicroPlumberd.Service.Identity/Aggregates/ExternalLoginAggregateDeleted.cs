namespace MicroPlumberd.Service.Identity.Aggregates;

[OutputStream("ExternalLogin")]
public record ExternalLoginAggregateDeleted
{
    public Guid Id { get; init; } = Guid.NewGuid();
}