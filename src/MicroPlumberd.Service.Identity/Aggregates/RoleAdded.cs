namespace MicroPlumberd.Service.Identity.Aggregates;

[OutputStream("Authorization")]
public record RoleAdded
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public RoleIdentifier RoleId { get; init; }
    public string ConcurrencyStamp { get; init; }
}