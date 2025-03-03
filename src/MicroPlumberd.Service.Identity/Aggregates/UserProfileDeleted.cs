namespace MicroPlumberd.Services.Identity.Aggregates;

[OutputStream("UserProfile")]
public record UserProfileDeleted
{
    public Guid Id { get; init; } = Guid.NewGuid();
}