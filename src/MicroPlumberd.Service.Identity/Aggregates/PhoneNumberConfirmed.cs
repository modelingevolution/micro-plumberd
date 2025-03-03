namespace MicroPlumberd.Services.Identity.Aggregates;

[OutputStream("UserProfile")]
public record PhoneNumberConfirmed
{
    public Guid Id { get; init; } = Guid.NewGuid();

}