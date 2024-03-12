using EventStore.Client;

namespace MicroPlumberd;

/// <summary>
/// Root interface for plumber
/// </summary>
public interface IPlumber
{
    /// <summary>
    /// Appends event to a stream, uses relevant convention, however aggregate-type or instance are passed as null to conventions.
    /// </summary>
    /// <param name="streamId"></param>
    /// <param name="rev"></param>
    /// <param name="events"></param>
    /// <param name="metadata"></param>
    /// <returns></returns>
    Task AppendEvents(string streamId, StreamRevision rev, IEnumerable<object> events, object? metadata = null);
    Task AppendEvents(string streamId, StreamState state, IEnumerable<object> events, object? metadata = null);
    ISubscriptionSet SubscribeSet();
    ISubscriptionRunner Subscribe(string streamName, FromStream start, UserCredentials? userCredentials = null, CancellationToken cancellationToken = new CancellationToken());

    Task<IAsyncDisposable> SubscribeModel<TModel>(TModel model, FromStream? start = null) where TModel : IReadModel, ITypeRegister;

    Task<IAsyncDisposable> SubscribeModelPersistently<TModel>(TModel model) where TModel : IReadModel, ITypeRegister;

    ISubscriptionRunner SubscribePersistently(string streamName, string groupName, int bufferSize = 10, UserCredentials? userCredentials = null, CancellationToken cancellationToken = new CancellationToken());

    Task<T> Get<T>(Guid id) where T : IAggregate<T>, ITypeRegister;
    Task SaveChanges<T>(T aggregate, object? metadata = null) where T : IAggregate<T>;
    Task SaveNew<T>(T aggregate, object? metadata = null) where T : IAggregate<T>;
}