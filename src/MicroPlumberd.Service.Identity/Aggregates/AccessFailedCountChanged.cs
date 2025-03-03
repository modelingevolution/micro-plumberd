namespace MicroPlumberd.Service.Identity.Aggregates;

[OutputStream("Identity")]
public record AccessFailedCountChanged
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public int AccessFailedCount { get; init; }
    
}