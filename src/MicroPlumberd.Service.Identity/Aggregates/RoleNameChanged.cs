namespace MicroPlumberd.Service.Identity.Aggregates;

public record RoleNameChanged
{
    public Guid Id { get; init; }
    public string Name { get; init; }
    public string NormalizedName { get; init; }
    public string ConcurrencyStamp { get; init; }
}