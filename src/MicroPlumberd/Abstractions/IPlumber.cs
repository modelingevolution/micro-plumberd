using EventStore.Client;
namespace MicroPlumberd;

/// <summary>
/// Root interface for plumber
/// </summary>
public interface IPlumber
{
    IPlumberConfig Config { get; }
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

    Task<IEventRecord<T>?> FindEventInStream<T>(string streamId, Guid id, TypeEventConverter eventMapping = null,
        Direction scanDirection = Direction.Backwards);
    Task<IEventRecord?> FindEventInStream(string streamId, Guid id, TypeEventConverter eventMapping,
        Direction scanDirection = Direction.Backwards);
    ISubscriptionSet SubscribeSet();
    ISubscriptionRunner Subscribe(string streamName, FromStream start, UserCredentials? userCredentials = null, CancellationToken cancellationToken = new CancellationToken());

    Task<IAsyncDisposable> SubscribeEventHandle<TEventHandler>(TEventHandler? eh=default,string? outputStream=null, FromStream? start = null) where TEventHandler : class,IEventHandler, ITypeRegister;

    Task<IAsyncDisposable> SubscribeEventHandlerPersistently<TEventHandler>(TEventHandler? model=null, string? outputStream = null, string? groupName = null, IPosition? startFrom = null) where TEventHandler : class,IEventHandler, ITypeRegister;

    ISubscriptionRunner SubscribePersistently(string streamName, string groupName, int bufferSize = 10, UserCredentials? userCredentials = null, CancellationToken cancellationToken = new CancellationToken());
    
    Task Rehydrate<T>(T model, string stream) where T : IEventHandler, ITypeRegister;
    Task Rehydrate<T>(T model, Guid id) where T : IEventHandler, ITypeRegister;

    Task<T> Get<T>(Guid id) where T : IAggregate<T>, ITypeRegister;
    Task SaveChanges<T>(T aggregate, object? metadata = null) where T : IAggregate<T>;
    Task SaveNew<T>(T aggregate, object? metadata = null) where T : IAggregate<T>;
    Task AppendLink(string streamId, Metadata metadata);
}