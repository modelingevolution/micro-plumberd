namespace MicroPlumberd.Service.Identity.Aggregates;

public record PhoneNumberChanged
{
    public Guid Id { get; init; }
    public string PhoneNumber { get; init; }
    public string ConcurrencyStamp { get; init; }
}