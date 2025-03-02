namespace MicroPlumberd.Service.Identity.Aggregates;

public record UserProfileCreated
{
    public Guid Id { get; init; }
    public UserIdentifier UserId { get; init; }
    public string UserName { get; init; }
    public string NormalizedUserName { get; init; }
    public string Email { get; init; }
    public string NormalizedEmail { get; init; }
    public string PhoneNumber { get; init; }
    public string ConcurrencyStamp { get; init; }
}