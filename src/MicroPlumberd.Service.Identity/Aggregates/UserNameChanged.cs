namespace MicroPlumberd.Services.Identity.Aggregates;

[OutputStream("UserProfile")]
public record UserNameChanged
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string UserName { get; init; }
    public string NormalizedUserName { get; init; }
    
}