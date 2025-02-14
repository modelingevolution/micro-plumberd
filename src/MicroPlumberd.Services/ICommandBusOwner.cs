namespace MicroPlumberd.Services;

public interface ICommandBusOwner : IDisposable
{
    Task SendAsync(object recipientId, object command, TimeSpan? timeout = null, bool fireAndForget = false,
        CancellationToken token = default);
}