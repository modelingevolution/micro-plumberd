namespace MicroPlumberd.Services.Identity.Aggregates;

[OutputStream("UserProfile")]
public record UserProfileCreated
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string UserName { get; init; }
    public string NormalizedUserName { get; init; }
    public string Email { get; init; }
    public string NormalizedEmail { get; init; }
    public string PhoneNumber { get; init; }
    
}