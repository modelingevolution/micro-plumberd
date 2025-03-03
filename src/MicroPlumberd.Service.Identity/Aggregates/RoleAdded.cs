namespace MicroPlumberd.Services.Identity.Aggregates;

[OutputStream("Authorization")]
public record RoleAdded
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public RoleIdentifier RoleId { get; init; }
    
}