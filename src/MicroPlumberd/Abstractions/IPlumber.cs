using EventStore.Client;

namespace MicroPlumberd;

public interface IPlumber
{
    Task AppendEvents(string streamId, StreamRevision rev, IEnumerable<object> events, object? metadata = null);
    Task AppendEvents(string streamId, StreamState state, IEnumerable<object> events, object? metadata = null);
    ISubscriptionSet SubscribeSet();
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