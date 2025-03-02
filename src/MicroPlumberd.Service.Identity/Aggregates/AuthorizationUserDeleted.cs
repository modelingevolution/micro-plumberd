namespace MicroPlumberd.Service.Identity.Aggregates;

[OutputStream("Authorization")]
public record AuthorizationUserDeleted
{
    public Guid Id { get; init; } = Guid.NewGuid();
}