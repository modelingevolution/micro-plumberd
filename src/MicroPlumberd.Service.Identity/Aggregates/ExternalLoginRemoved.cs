namespace MicroPlumberd.Services.Identity.Aggregates;

[OutputStream("ExternalLogin")]
public record ExternalLoginRemoved
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public ExternalLoginProvider Provider { get; init; }
    public ExternalLoginKey ProviderKey { get; init; }
    
}