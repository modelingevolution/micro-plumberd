namespace MicroPlumberd.Service.Identity.Aggregates;

public record RoleConcurrencyStampChanged
{
    public Guid Id { get; init; }
    public string ConcurrencyStamp { get; init; }
}