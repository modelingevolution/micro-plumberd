namespace MicroPlumberd.Services.Identity.Aggregates;

[OutputStream("Identity")]
public record LockoutEnabledChanged
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public bool LockoutEnabled { get; init; }
    
}