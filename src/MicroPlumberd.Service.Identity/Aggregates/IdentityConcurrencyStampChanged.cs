namespace MicroPlumberd.Service.Identity.Aggregates;

[OutputStream("Identity")]
public record IdentityConcurrencyStampChanged
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string ConcurrencyStamp { get; init; }
}