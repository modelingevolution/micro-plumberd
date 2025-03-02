namespace MicroPlumberd.Service.Identity.Aggregates;

public record UserProfileDeleted
{
    public Guid Id { get; init; }
}