namespace MicroPlumberd.Services.Identity.Aggregates;

[OutputStream("Identity")]
public record LockoutEndChanged
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset? LockoutEnd { get; init; }
    
}