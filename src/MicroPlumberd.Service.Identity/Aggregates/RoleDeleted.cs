namespace MicroPlumberd.Service.Identity.Aggregates;

public record RoleDeleted
{
    public Guid Id { get; init; }
}