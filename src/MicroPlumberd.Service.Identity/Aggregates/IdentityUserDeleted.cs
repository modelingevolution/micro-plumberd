namespace MicroPlumberd.Service.Identity.Aggregates;

[OutputStream("Identity")]
public record IdentityUserDeleted
{
    public Guid Id { get; init; } = Guid.NewGuid();
}