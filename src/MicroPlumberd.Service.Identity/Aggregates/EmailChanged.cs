namespace MicroPlumberd.Service.Identity.Aggregates;

public record EmailChanged
{
    public Guid Id { get; init; }
    public string Email { get; init; }
    public string NormalizedEmail { get; init; }
    public string ConcurrencyStamp { get; init; }
}