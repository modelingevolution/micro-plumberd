namespace MicroPlumberd;

public interface ICommandBus
{
    Task SendAsync(Guid recipientId, object command);
}