namespace MicroPlumberd.Service.Identity.Aggregates;

[OutputStream("Identity")]
public record PasswordChanged
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string PasswordHash { get; init; }
    public string SecurityStamp { get; init; }
    
}