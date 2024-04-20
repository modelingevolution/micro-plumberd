using EventStore.Client;
namespace MicroPlumberd;

/// <summary>
/// Represents a snapshot object used in Plumberd.
/// </summary>
public interface ISnapshot
{
    /// <summary>
    /// Gets the data of the snapshot.
    /// </summary>
    object Data { get; }
    
    /// <summary>
    /// Gets the creation date of the snapshot.
    /// </summary>
    DateTimeOffset Created { get; }
    
    /// <summary>
    /// Gets the version of the snapshot.
    /// </summary>
    long Version { get; }
}

public record State<T>(T Value, Metadata Metadata)
{
    public static implicit operator T(State<T> st) => st.Value;
}
/// <summary>
/// Represents a snapshot object used in Plumberd.
/// </summary>
public abstract record Snapshot
{
    internal abstract object Value { get; set; }
    
    /// <summary>
    /// Gets the creation date of the snapshot.
    /// </summary>
    public DateTimeOffset Created { get; internal set; }
    
    /// <summary>
    /// Gets the version of the snapshot.
    /// </summary>
    public long Version { get; internal set; }
}

/// <summary>
/// Represents a generic snapshot object used in Plumberd.
/// </summary>
/// <typeparam name="T">The type of the snapshot data.</typeparam>
public sealed record Snapshot<T> : Snapshot, ISnapshot
{
    object ISnapshot.Data => Data;
    
    /// <summary>
    /// Gets the data of the snapshot.
    /// </summary>
    public T Data { get; internal set; }

    public static implicit operator T(Snapshot<T> st) => st.Data;
    internal override object Value
    {
        get => Data;
        set => Data = (T)value;
    }
}

/// <summary>
/// Root interface for plumberd
/// </summary>
public interface IPlumber
{
    /// <summary>
    /// Plubers configuration.
    /// </summary>
    IPlumberReadOnlyConfig Config { get; }
    
    /// <summary>
    /// EventStore's client
    /// </summary>
    EventStoreClient Client { get; }
    /// <summary>
    /// EventStore's persistent subsctiption client
    /// </summary>
    EventStorePersistentSubscriptionsClient PersistentSubscriptionClient { get; }
    /// <summary>
    /// EventStore's projection managemenet client
    /// </summary>
    EventStoreProjectionManagementClient ProjectionManagementClient { get; }
    /// <summary>
    /// Projection's register, responsible for caching information about projection from EventStore.
    /// </summary>
    IProjectionRegister ProjectionRegister { get; }
    /// <summary>
    /// Metadata information about registered event-handlers.
    /// </summary>
    ITypeHandlerRegisters TypeHandlerRegisters { get; }

    /// <summary>
    /// Appends event to a stream, uses relevant convention to create metadata.
    /// </summary>
    /// <param name="streamId">Full stream id, typically in format {category}-{id}</param>
    /// <param name="rev">Expected stream revision</param>
    /// <param name="events">Events that are going to be serialized and appended</param>
    /// <param name="metadata">Metadata that will be merged with metadata created from conventions</param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task<IWriteResult> AppendEvents(string streamId, StreamRevision rev, IEnumerable<object> events,
        object? metadata = null, CancellationToken token = default);

    /// <summary>
    /// Appends event to a stream, uses relevant convention to create metadata.
    /// </summary>
    /// <param name="streamId">Full name of streamId for example: 'TicketBooked-b27f9322-7d73-4d98-a605-a731a2c373c6'</param>
    /// <param name="evt">Event object</param>
    /// <param name="state">Expected state of the stream</param>
    /// <param name="evtName">Name of the event</param>
    /// <param name="metadata">Additional metadata, can be null</param>
    /// <returns></returns>
    Task<IWriteResult> AppendEventToStream(string streamId, object evt, StreamState? state = null, string? evtName = null,
        object? metadata = null, CancellationToken token = default);

    /// <summary>
    /// Appends event to a stream, uses relevant convention to create metadata.
    /// </summary>
    /// <param name="streamId">Full stream id, typically in format {category}-{id}</param>
    /// <param name="state">State of the stream</param>
    /// <param name="events">Events that are going to be serialized and appended</param>
    /// <param name="metadata">Metadata that will be merged with metadata created from conventions</param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task<IWriteResult> AppendEvents(string streamId, StreamState state, IEnumerable<object> events,
        object? metadata = null, CancellationToken token = default);

    /// <summary>
    /// Appends event to a stream, uses relevant convention to create metadata.
    /// </summary>
    /// <param name="streamId">Full name of streamId for example: 'TicketBooked-b27f9322-7d73-4d98-a605-a731a2c373c6'</param>
    /// <param name="state"></param>
    /// <param name="events"></param>
    /// <returns></returns>
    Task AppendEvents(string streamId, StreamState state, params object[] events) => AppendEvents(streamId, state, events,null);

    /// <summary>
    /// Finds the event in the stream.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="id">The identifier of the event.</param>
    /// <param name="eventMapping">The event mapping.</param>
    /// <param name="scanDirection">The scan direction.</param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task<IEventRecord<T>?> FindEventInStream<T>(string streamId, Guid id, TypeEventConverter eventMapping = null,
        Direction scanDirection = Direction.Backwards, CancellationToken token = default);

    /// <summary>
    /// Finds the event in the stream.
    /// </summary>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="id">The identifier of the event.</param>
    /// <param name="eventMapping">The event mapping.</param>
    /// <param name="scanDirection">The scan direction.</param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task<IEventRecord?> FindEventInStream(string streamId, Guid id, TypeEventConverter eventMapping,
        Direction scanDirection = Direction.Backwards, CancellationToken token = default);

    /// <summary>
    /// Returns a builder for creating composition of projections subscribed to a stream.
    /// </summary>
    /// <returns></returns>
    ISubscriptionSet SubscribeSet();

    /// <summary>
    /// Subscribes the specified stream name.
    /// </summary>
    /// <param name="streamName">Name of the stream.</param>
    /// <param name="start">The start position</param>
    /// <param name="userCredentials">The user credentials.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns></returns>
    ISubscriptionRunner Subscribe(string streamName, FromRelativeStreamPosition start, UserCredentials? userCredentials = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes the event handler. EventHandler is a class that contains many overloaded 'Given' methods. A projection will be created at EventStore that creates a joined stream from all supported event-types by EventHandler.
    /// Then EventHandler subscribe the the output stream.
    /// </summary>
    /// <typeparam name="TEventHandler">The type of the event handler.</typeparam>
    /// <param name="mapFunc">The map function.</param>
    /// <param name="eventTypes">Supported event types.</param>
    /// <param name="eh">The event-handler</param>
    /// <param name="outputStream">The output stream.</param>
    /// <param name="start">The start of the stream</param>
    /// <param name="ensureOutputStreamProjection">if set to <c>true</c> [ensure output stream projection].</param>
    /// <returns></returns>
    Task<IAsyncDisposable> SubscribeEventHandler<TEventHandler>(TypeEventConverter mapFunc,
        IEnumerable<string>? eventTypes,
        TEventHandler? eh = default, string? outputStream = null,
        FromStream? start = null, 
        bool ensureOutputStreamProjection = true) 
        where TEventHandler:class,IEventHandler;


    /// <summary>
    /// Subscribes the event handler. EventHandler is a class that contains many overloaded 'Given' methods. A projection will be created at EventStore that creates a joined stream from all supported event-types by EventHandler.
    /// Then EventHandler subscribe the output stream.
    /// </summary>
    /// <typeparam name="TEventHandler">The type of the event handler.</typeparam>
    /// <param name="eh">The event-handler/model</param>
    /// <param name="outputStream">The output stream.</param>
    /// <param name="start">The start.</param>
    /// <param name="ensureOutputStreamProjection">if set to <c>true</c> [ensure output stream projection].</param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task<IAsyncDisposable> SubscribeEventHandler<TEventHandler>(TEventHandler? eh = default, string? outputStream = null, FromRelativeStreamPosition? start = null, bool ensureOutputStreamProjection = true, CancellationToken token = default)
        where TEventHandler : class, IEventHandler, ITypeRegister;


    /// <summary>
    /// Subscribes the event handler persistently. EventHandler is a class that contains many overloaded 'Given' methods. A projection will be created at EventStore that creates a joined stream from all supported event-types by EventHandler.
    /// Then EventHandler subscribe the the output stream.
    /// </summary>
    /// <typeparam name="TEventHandler">The type of the event handler.</typeparam>
    /// <param name="model">Optional event-handler/model.</param>
    /// <param name="outputStream">Optional output stream.</param>
    /// <param name="groupName">Optional group name.</param>
    /// <param name="startFrom">Optional start of the stream.</param>
    /// <param name="ensureOutputStreamProjection">when true creates projection that creates output's stream</param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task<IAsyncDisposable> SubscribeEventHandlerPersistently<TEventHandler>(TEventHandler? model=null, string? outputStream = null, string? groupName = null, IPosition? startFrom = null, bool ensureOutputStreamProjection = true, CancellationToken token = default) where TEventHandler : class,IEventHandler, ITypeRegister;

    /// <summary>
    /// Returns a subscription builder that will subscribe model persistently.
    /// </summary>
    /// <param name="streamName">Name of the stream.</param>
    /// <param name="groupName">Name of the group.</param>
    /// <param name="bufferSize">Size of the buffer.</param>
    /// <param name="userCredentials">The user credentials.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns></returns>
    ISubscriptionRunner SubscribePersistently(string streamName, string groupName, int bufferSize = 10, UserCredentials? userCredentials = null, CancellationToken cancellationToken = new CancellationToken());

    /// <summary>
    /// Rehydrates the specified model.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="model">The model.</param>
    /// <param name="stream">The stream.</param>
    /// <param name="position">The position.</param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task Rehydrate<T>(T model, string stream, StreamPosition? position = null, CancellationToken token = default) where T : IEventHandler, ITypeRegister;

    /// <summary>
    /// Rehydrates the specified model
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="model">The model.</param>
    /// <param name="id">The identifier.</param>
    /// <param name="position">The position from which reply events.</param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task Rehydrate<T>(T model, Guid id, StreamPosition? position = null, CancellationToken token = default) where T : IEventHandler, ITypeRegister;

    /// <summary>
    /// Returns the aggregate identified by id.
    /// This usually mean that all the event will be loaded from the EventStoreDB and executed through 'Given' method on it's instance. 
    /// If the aggregate supports snapshoting, it's state will be loaded from latest snapshot and relevant events from that time will be replied on it's instance.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="id">The identifier.</param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task<T> Get<T>(object id, CancellationToken token = default) where T : IAggregate<T>, ITypeRegister,IId;

    /// <summary>
    /// Saves all pending events from the aggregate. Uses optimistic concurrency.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="aggregate">The aggregate.</param>
    /// <param name="metadata">The optional metadata.</param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task<IWriteResult> SaveChanges<T>(T aggregate, object? metadata = null, CancellationToken token = default) where T : IAggregate<T>, IId;

    /// <summary>
    /// Saves the aggregate. Expects that no aggregate exists. 
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="aggregate">The aggregate.</param>
    /// <param name="metadata">The optional metadata.</param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task<IWriteResult> SaveNew<T>(T aggregate, object? metadata = null, CancellationToken token = default) where T : IAggregate<T>, IId;

    /// <summary>
    /// Gets the snapshot - deserializes snapshot from the stream. Stream is identified by typeof(T). Deserialization is done from the latest event (snaphost) in the stream.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="id">The identifier.</param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task<Snapshot<T>?> GetSnapshot<T>(Guid id, CancellationToken token = default);

    /// <summary>
    /// Gets the snapshot - deserializes snapshot from the stream. Stream is identified by snaphostType. Deserialization is done from the latest event (snaphost) in the stream.
    /// </summary>
    /// <param name="id">The identifier.</param>
    /// <param name="snapshotType">Type of the snapshot.</param>
    /// <param name="token"></param>
    /// <returns>The snapshot information containing the snaphost and relevant metadata.</returns>
    Task<Snapshot?> GetSnapshot(object id, Type snapshotType, CancellationToken token = default);

    /// <summary>
    /// Appends the link to a stream.
    /// </summary>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="metadata">The metadata.</param>
    /// <param name="state">The expected state of the stream</param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task<IWriteResult> AppendLink(string streamId, Metadata metadata, StreamState? state = null,
        CancellationToken token = default);

    /// <summary>
    /// Subscribes the event handler persistently. This means that at least once an event is processed successfully, it wont be processed anymore.
    /// </summary>
    /// <typeparam name="TEventHandler">The type of the event handler.</typeparam>
    /// <param name="mapFunc">The map function.</param>
    /// <param name="events">The events.</param>
    /// <param name="model">The model.</param>
    /// <param name="outputStream">The output stream.</param>
    /// <param name="groupName">Name of the group.</param>
    /// <param name="startFrom">The start from.</param>
    /// <param name="ensureOutputStreamProjection">if set to <c>true</c> [ensure output stream projection].</param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task<IAsyncDisposable> SubscribeEventHandlerPersistently<TEventHandler>(TypeEventConverter mapFunc,
        IEnumerable<string>? events,
        TEventHandler? model,
        string? outputStream = null, string? groupName = null, IPosition? startFrom = null,
        bool ensureOutputStreamProjection = true, CancellationToken token = default)
        where TEventHandler : class, IEventHandler;

    /// <summary>
    /// Reads stream and returns events.
    /// </summary>
    /// <typeparam name="TOwner">The type of the owner (aggregate).</typeparam>
    /// <param name="id">The identifier (of the aggregate).</param>
    /// <param name="start">The stream start position.</param>
    /// <param name="direction">The direction of the reading.</param>
    /// <param name="maxCount">The maximum number of read events.</param>
    /// <param name="token"></param>
    /// <returns></returns>
    IAsyncEnumerable<object> Read<TOwner>(object id, StreamPosition? start = null, Direction? direction = null,
        long maxCount = 9223372036854775807L, CancellationToken token = default) where TOwner : ITypeRegister;

    /// <summary>
    /// Reads stream and returns events.
    /// </summary>
    /// <typeparam name="TOwner">The type of the owner(aggregate).</typeparam>
    /// <param name="start">The stream start position.</param>
    /// <param name="direction">The direction of the reading.</param>
    /// <param name="maxCount">The maximum number of read events.</param>
    /// <param name="token"></param>
    /// <returns></returns>
    IAsyncEnumerable<object> Read<TOwner>(StreamPosition? start = null, Direction? direction = null,
        long maxCount = 9223372036854775807L, CancellationToken token = default) where TOwner : ITypeRegister;

    /// <summary>
    /// Reads stream and returns events.
    /// </summary>
    /// <param name="streamId">The full stream name</param>
    /// <param name="converter">The event-map converter.</param>
    /// <param name="start">The stream start position.</param>
    /// <param name="direction">The direction of the reading.</param>
    /// <param name="maxCount">The maximum number of read events.</param>
    /// <param name="token"></param>
    /// <returns></returns>
    IAsyncEnumerable<object> Read(string streamId, TypeEventConverter converter, StreamPosition? start = null,
        Direction? direction = null, long maxCount = 9223372036854775807L, CancellationToken token = default);

    /// <summary>
    /// Reads stream and returns event and metadata information.
    /// </summary>
    /// <param name="streamId">The full stream name</param>
    /// <param name="converter">The event-map converter.</param>
    /// <param name="start">The stream start position.</param>
    /// <param name="direction">The direction of the reading.</param>
    /// <param name="maxCount">The maximum number of read events.</param>
    /// <param name="token"></param>
    /// <returns></returns>
    IAsyncEnumerable<(object, Metadata)> ReadFull(string streamId, TypeEventConverter converter,
        StreamPosition? start = null, Direction? direction = null, long maxCount = 9223372036854775807L,
        CancellationToken token = default);


    /// <summary>
    /// Appends the snapshot to a stream determined by the type of the snapshot/state.
    /// </summary>
    /// <param name="snapshot">The snapshot.</param>
    /// <param name="id">The identifier of the snapshot/state.</param>
    /// <param name="version">The expected version.</param>
    /// <param name="state">The expected state of the stream.</param>
    /// <returns></returns>
    Task<IWriteResult> AppendSnapshot(object snapshot, object id, long version, StreamState? state = null, CancellationToken token = default);

    /// <summary>
    /// Appends the event. StreamId is determined using conventions.
    /// </summary>
    /// <param name="evt">The evt.</param>
    /// <param name="id">The identifier of stream.(second segment of typical streamId, So if streamId is 'foo-123', 123 would be the id.)</param>
    /// <param name="metadata">Optional metadata.</param>
    /// <param name="state">Expected state.</param>
    /// <param name="evtName">Optional name of the event.</param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task<IWriteResult> AppendEvent(object evt, object? id = null, object? metadata = null, StreamState? state = null,
        string? evtName = null, CancellationToken token = default);

    /// <summary>
    /// Appends the link.
    /// </summary>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="streamPosition">Stream position in original stream</param>
    /// <param name="streamSourceId">The stream source identifier.</param>
    /// <param name="state">Optional expected stream state.</param>
    /// <returns></returns>
    Task<IWriteResult> AppendLink(string streamId, ulong streamPosition, string streamSourceId,
        StreamState? state = null, CancellationToken token = default);

    /// <summary>
    /// Updates or adds simple entity/state. Be aware, that rdb constraints are not possible.
    /// </summary>
    /// <param name="state">The entity.</param>
    /// <param name="id">The identifier.</param>
    /// <param name="version">The version.</param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task<IWriteResult> AppendState(object state, object id, long? version, CancellationToken token = default);

    /// <summary>
    /// Updates or adds simple entity/state. Be aware, that rdb constraints are not possible.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="state">The entity.</param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task<IWriteResult> AppendState<T>(T state, CancellationToken token = default) where T:IId;

    Task<State<T>?> GetState<T>(object id, string? streamId = null, CancellationToken token = default) where T:class;
}