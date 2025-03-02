namespace MicroPlumberd.Service.Identity.Aggregates;

[OutputStream("ExternalLogin")]
public record ExtenralConcurrencyStampChanged
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string ConcurrencyStamp { get; init; }
}