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
    /// <param name="streamId">Full stream id, typically in format {category}-{id}</param>
    /// <param name="rev">Expected stream revision</param>
    /// <param name="events">Events that are going to be serialized and appended</param>
    /// <param name="metadata">Metadata that will be merged with metadata created from conventions</param>
    /// <returns></returns>
    Task AppendEvents(string streamId, StreamRevision rev, IEnumerable<object> events, object? metadata = null);

    /// <summary>
    /// Appends event to a stream, uses relevant convention, however aggregate-type or instance are passed as null to conventions.
    /// </summary>
    /// <param name="streamId">Full stream id, typically in format {category}-{id}</param>
    /// <param name="state">State of the stream</param>
    /// <param name="events">Events that are going to be serialized and appended</param>
    /// <param name="metadata">Metadata that will be merged with metadata created from conventions</param>
    /// <returns></returns>
    Task AppendEvents(string streamId, StreamState state, IEnumerable<object> events, object? metadata = null);
    Task AppendEvents(string streamId, StreamState state, params object[] events) => AppendEvents(streamId, state, events,null);

    ISubscriptionSet SubscribeSet();
    ISubscriptionRunner Subscribe(string streamName, FromStream start, UserCredentials? userCredentials = null, CancellationToken cancellationToken = new CancellationToken());

    Task<IAsyncDisposable> SubscribeModel<TModel>(TModel model, FromStream? start = null) where TModel : IReadModel, ITypeRegister;

    Task<IAsyncDisposable> SubscribeModelPersistently<TModel>(TModel model) where TModel : IReadModel, ITypeRegister;

    ISubscriptionRunner SubscribePersistently(string streamName, string groupName, int bufferSize = 10, UserCredentials? userCredentials = null, CancellationToken cancellationToken = new CancellationToken());

    Task<T> Get<T>(Guid id) where T : IAggregate<T>, ITypeRegister;
    Task SaveChanges<T>(T aggregate, object? metadata = null) where T : IAggregate<T>;
    Task SaveNew<T>(T aggregate, object? metadata = null) where T : IAggregate<T>;
}