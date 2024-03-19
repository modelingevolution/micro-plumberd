namespace MicroPlumberd;

public record CommandInvocationFailed
{
    public Guid RecipientId { get; init; }
    public ICommandRequest Command { get; init; }
    public string Message { get; init; }
}