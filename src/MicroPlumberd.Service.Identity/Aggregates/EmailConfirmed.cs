namespace MicroPlumberd.Service.Identity.Aggregates;

public record EmailConfirmed
{
    public Guid Id { get; init; }
    public string ConcurrencyStamp { get; init; }
}