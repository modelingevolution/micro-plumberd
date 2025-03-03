namespace MicroPlumberd.Services.Identity.Aggregates;

public record UserProfileDeleted
{
    public Guid Id { get; init; }
}