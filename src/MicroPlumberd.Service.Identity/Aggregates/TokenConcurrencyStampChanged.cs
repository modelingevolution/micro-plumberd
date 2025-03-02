namespace MicroPlumberd.Service.Identity.Aggregates;

[OutputStream("Token")]
public record TokenConcurrencyStampChanged
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string ConcurrencyStamp { get; init; }
}