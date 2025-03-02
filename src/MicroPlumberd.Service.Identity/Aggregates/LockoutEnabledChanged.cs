namespace MicroPlumberd.Service.Identity.Aggregates;

[OutputStream("Identity")]
public record LockoutEnabledChanged
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public bool LockoutEnabled { get; init; }
    public string ConcurrencyStamp { get; init; }
}