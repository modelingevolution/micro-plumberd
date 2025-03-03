namespace MicroPlumberd.Services.Identity.Aggregates;

[OutputStream("Identity")]
public record SecurityStampChanged
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string SecurityStamp { get; init; }
    
}