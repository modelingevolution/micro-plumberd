namespace MicroPlumberd.Services.Identity.Aggregates;

[OutputStream("Authorization")]
public record RoleDeleted
{
    public Guid Id { get; init; }
}