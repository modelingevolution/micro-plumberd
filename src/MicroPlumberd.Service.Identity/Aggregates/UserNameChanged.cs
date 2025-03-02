namespace MicroPlumberd.Service.Identity.Aggregates;

public record UserNameChanged
{
    public Guid Id { get; init; }
    public string UserName { get; init; }
    public string NormalizedUserName { get; init; }
    public string ConcurrencyStamp { get; init; }
}