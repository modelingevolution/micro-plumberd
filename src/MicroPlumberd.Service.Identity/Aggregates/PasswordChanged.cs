namespace MicroPlumberd.Services.Identity.Aggregates;

[OutputStream("Identity")]
public record PasswordChanged
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string PasswordHash { get; init; }
    
    
}