namespace MicroPlumberd.Services.Identity.Aggregates;

[OutputStream("UserProfile")]
public record PhoneNumberChanged
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string PhoneNumber { get; init; }
    
}