using EventStore.Client;

namespace MicroPlumberd;

public interface IPlumber
{
    ISubscriptionRunner Subscribe(string streamName, FromStream start,
        UserCredentials? userCredentials = null, CancellationToken cancellationToken = new CancellationToken());

    Task<IAsyncDisposable> SubscribeModel<TModel>(TModel model, FromStream? start = null)
        where TModel : IReadModel, ITypeRegister;

    Task<IAsyncDisposable> SubscribeModelPersistently<TModel>(TModel model)
        where TModel : IReadModel, ITypeRegister;

    ISubscriptionRunner SubscribePersistently(string streamName, string groupName, int bufferSize = 10,
        UserCredentials? userCredentials = null, CancellationToken cancellationToken = new CancellationToken());

    Task<T> Get<T>(Guid id)
        where T : IAggregate<T>, ITypeRegister;

    Task SaveChanges<T>(T aggregate, object? metadata = null)
        where T : IAggregate<T>;

    Task SaveNew<T>(T aggregate, object? metadata = null)
        where T : IAggregate<T>;
}