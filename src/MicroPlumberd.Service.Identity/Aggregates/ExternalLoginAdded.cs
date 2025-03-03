namespace MicroPlumberd.Service.Identity.Aggregates;

[OutputStream("ExternalLogin")]
public record ExternalLoginAdded
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public ExternalLoginProvider Provider { get; init; }
    public ExternalLoginKey ProviderKey { get; init; }
    public string DisplayName { get; init; }
    
}