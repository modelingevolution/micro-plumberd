namespace MicroPlumberd.Services.Identity.Aggregates;

[OutputStream("Role")]
public record RoleNameChanged
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; }
    public string NormalizedName { get; init; }
    
}