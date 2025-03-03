namespace MicroPlumberd.Services.Identity.Aggregates;

[OutputStream("UserProfile")]
public record EmailConfirmed
{
    public Guid Id { get; init; } = Guid.NewGuid();

}