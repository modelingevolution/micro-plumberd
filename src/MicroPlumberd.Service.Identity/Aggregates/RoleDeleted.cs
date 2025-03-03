namespace MicroPlumberd.Service.Identity.Aggregates;

[OutputStream("Authorization")]
public record RoleDeleted
{
    public Guid Id { get; init; }
}