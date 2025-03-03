namespace MicroPlumberd.Service.Identity.Aggregates;

[OutputStream("Authorization")]
public record ClaimRemoved
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public ClaimType ClaimType { get; init; }
    public ClaimValue ClaimValue { get; init; }
    
}