namespace MicroPlumberd;

public interface ICommandEnqueued
{
    object Command { get; }
    Guid RecipientId { get; }
}