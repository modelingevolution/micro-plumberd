namespace MicroPlumberd.Services.Identity.Aggregates;

[OutputStream("Authorization")]
public record RoleRemoved
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public RoleIdentifier RoleId { get; init; }
    
}