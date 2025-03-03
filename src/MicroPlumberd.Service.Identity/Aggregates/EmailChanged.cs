namespace MicroPlumberd.Services.Identity.Aggregates;

[OutputStream("UserProfile")]
public record EmailChanged
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Email { get; init; }
    public string NormalizedEmail { get; init; }
    
}