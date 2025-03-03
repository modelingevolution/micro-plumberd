namespace MicroPlumberd.Service.Identity.Aggregates;

[OutputStream("Identity")]
public record AuthenticatorKeyChanged
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string AuthenticatorKey { get; init; }
    
}