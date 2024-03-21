using EventStore.Client;
namespace MicroPlumberd;

/// <summary>
/// Root interface for plumber
/// </summary>
public interface IPlumber
{
    IPlumberConfig Config { get; }
    EventStoreClient Client { get; }
    EventStorePersistentSubscriptionsClient PersistentSubscriptionClient { get; }
    EventStoreProjectionManagementClient ProjectionManagementClient { get; }
    IProjectionRegister ProjectionRegister { get; }

    /// <summary>
    /// Appends event to a stream, uses relevant convention, however aggregate-type or instance are passed as null to conventions.
    /// </summary>
    /// <param name="streamId">Full stream id, typically in format {category}-{id}</param>
    /// <param name="rev">Expected stream revision</param>
    /// <param name="events">Events that are going to be serialized and appended</param>
    /// <param name="metadata">Metadata that will be merged with metadata created from conventions</param>
    /// <returns></returns>
    Task<IWriteResult> AppendEvents(string streamId, StreamRevision rev, IEnumerable<object> events,
        object? metadata = null);

    Task<IWriteResult> AppendEvent(string streamId, StreamState state, string evtName, object evt, object? metadata = null);
    /// <summary>
    /// Appends event to a stream, uses relevant convention, however aggregate-type or instance are passed as null to conventions.
    /// </summary>
    /// <param name="streamId">Full stream id, typically in format {category}-{id}</param>
    /// <param name="state">State of the stream</param>
    /// <param name="events">Events that are going to be serialized and appended</param>
    /// <param name="metadata">Metadata that will be merged with metadata created from conventions</param>
    /// <returns></returns>
    Task<IWriteResult> AppendEvents(string streamId, StreamState state, IEnumerable<object> events,
        object? metadata = null);
    Task AppendEvents(string streamId, StreamState state, params object[] events) => AppendEvents(streamId, state, events,null);

    Task<IEventRecord<T>?> FindEventInStream<T>(string streamId, Guid id, TypeEventConverter eventMapping = null,
        Direction scanDirection = Direction.Backwards);
    Task<IEventRecord?> FindEventInStream(string streamId, Guid id, TypeEventConverter eventMapping,
        Direction scanDirection = Direction.Backwards);
    ISubscriptionSet SubscribeSet();
    ISubscriptionRunner Subscribe(string streamName, FromStream start, UserCredentials? userCredentials = null, CancellationToken cancellationToken = new CancellationToken());

    Task<IAsyncDisposable> SubscribeEventHandler<TEventHandler>(TypeEventConverter mapFunc,
        IEnumerable<string>? eventTypes,
        TEventHandler? eh = default, string? outputStream = null,
        FromStream? start = null, bool ensureOutputStreamProjection = true) where TEventHandler:class,IEventHandler;
    Task<IAsyncDisposable> SubscribeEventHandler<TEventHandler>(TEventHandler? eh=default,string? outputStream=null, FromStream? start = null, bool ensureOutputStreamProjection = true) where TEventHandler : class,IEventHandler, ITypeRegister;

    Task<IAsyncDisposable> SubscribeEventHandlerPersistently<TEventHandler>(TEventHandler? model=null, string? outputStream = null, string? groupName = null, IPosition? startFrom = null, bool ensureOutputStreamProjection = true) where TEventHandler : class,IEventHandler, ITypeRegister;

    ISubscriptionRunner SubscribePersistently(string streamName, string groupName, int bufferSize = 10, UserCredentials? userCredentials = null, CancellationToken cancellationToken = new CancellationToken());
    
    Task Rehydrate<T>(T model, string stream) where T : IEventHandler, ITypeRegister;
    Task Rehydrate<T>(T model, Guid id) where T : IEventHandler, ITypeRegister;

    Task<T> Get<T>(Guid id) where T : IAggregate<T>, ITypeRegister;
    Task<IWriteResult> SaveChanges<T>(T aggregate, object? metadata = null) where T : IAggregate<T>;
    Task<IWriteResult> SaveNew<T>(T aggregate, object? metadata = null) where T : IAggregate<T>;
    Task<IWriteResult> AppendLink(string streamId, Metadata metadata, StreamState? state = null);

    Task<IAsyncDisposable> SubscribeEventHandlerPersistently<TEventHandler>(TypeEventConverter mapFunc,
        IEnumerable<string>? events,
        TEventHandler? model,
        string? outputStream = null, string? groupName = null, IPosition? startFrom = null, bool ensureOutputStreamProjection = true)
        where TEventHandler : class, IEventHandler;
}