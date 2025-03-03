namespace MicroPlumberd.Service.Identity.Aggregates;

[OutputStream("Authorization")]
public record RoleNameChanged
{
    public Guid Id { get; init; }
    public string Name { get; init; }
    public string NormalizedName { get; init; }
    
}