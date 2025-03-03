namespace MicroPlumberd.Services.Identity.Aggregates;

[OutputStream("Authorization")]
public record ClaimAdded
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public ClaimType ClaimType { get; init; }
    public ClaimValue ClaimValue { get; init; }
    
}