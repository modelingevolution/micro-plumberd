namespace MicroPlumberd.Services.Identity.Aggregates;

[OutputStream("Role")]
public record RoleDeleted
{
    public Guid Id { get; init; }= Guid.NewGuid();
}