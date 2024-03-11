using EventStore.Client;

namespace MicroPlumberd;

public interface IReadModel
{
    Task Given(Metadata m, object ev);
}
public interface ISubscriptionSet
{
    ISubscriptionSet With<TModel>(TModel model)
        where TModel : IReadModel, ITypeRegister;

    Task SubscribePersistentlyAsync(string outputStream, string? groupName = null);
    Task SubscribeAsync(string name, FromStream start);
}