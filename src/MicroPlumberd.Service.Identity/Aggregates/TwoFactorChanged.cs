namespace MicroPlumberd.Service.Identity.Aggregates;

[OutputStream("Identity")]
public record TwoFactorChanged
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public bool TwoFactorEnabled { get; init; }
    public string ConcurrencyStamp { get; init; }
}