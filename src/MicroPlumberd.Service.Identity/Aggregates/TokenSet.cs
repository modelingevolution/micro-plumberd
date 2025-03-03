namespace MicroPlumberd.Service.Identity.Aggregates;

[OutputStream("Token")]
public record TokenSet
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public TokenName Name { get; init; }
    public TokenValue Value { get; init; }
    public string LoginProvider { get; init; }
    
}