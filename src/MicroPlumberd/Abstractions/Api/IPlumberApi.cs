﻿using EventStore.Client;

namespace MicroPlumberd.Api;

/// <summary>
/// Root interface for plumberd
/// </summary>
public interface IPlumberApi
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
    /// <param name="ct"></param>
    /// <returns></returns>
    Task<IAsyncDisposable> SubscribeEventHandler<TEventHandler>(TypeEventConverter mapFunc,
        IEnumerable<string>? eventTypes,
        TEventHandler? eh = default, string? outputStream = null,
        FromStream? start = null, 
        bool ensureOutputStreamProjection = true, 
        CancellationToken ct = default) 
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
    Task<IAsyncDisposable> SubscribeEventHandlerPersistently<TEventHandler>(TEventHandler? model=null, string? outputStream = null, string? groupName = null, IPosition? startFrom = null, bool ensureOutputStreamProjection = true, int minCheckPointCount=1, CancellationToken token = default) 
        where TEventHandler : class,IEventHandler, ITypeRegister;

    /// <summary>
    /// Returns a subscription builder that will subscribe model persistently.
    /// </summary>
    /// <param name="streamName">Name of the stream.</param>
    /// <param name="groupName">Name of the group.</param>
    /// <param name="bufferSize">Size of the buffer.</param>
    /// <param name="userCredentials">The user credentials.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns></returns>
    ISubscriptionRunner SubscribePersistently(string streamName, string groupName, int bufferSize = 10, UserCredentials? userCredentials = null,CancellationToken cancellationToken = new CancellationToken());

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
    Task Rehydrate<T>(T model, Guid id, StreamPosition? position = null,  CancellationToken token = default) where T : IEventHandler, ITypeRegister;

    Task Rehydrate<T>(T model, string streamId, TypeEventConverter converter, StreamPosition? position = null,
        CancellationToken token = default)
        where T : IEventHandler;

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
    /// <param name="streamMetadata"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task<IWriteResult> SaveChanges<T>(T aggregate, object? metadata = null, StreamMetadata? streamMetadata = null, CancellationToken token = default) where T : IAggregate<T>, IId;

    /// <summary>
    /// Saves the aggregate. Expects that no aggregate exists. 
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="aggregate">The aggregate.</param>
    /// <param name="metadata">The optional metadata.</param>
    /// <param name="streamMetadata"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task<IWriteResult> SaveNew<T>(T aggregate, object? metadata = null, StreamMetadata? streamMetadata = null, CancellationToken token = default) where T : IAggregate<T>, IId;

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
        bool ensureOutputStreamProjection = true,int minCheckPointCount=1, CancellationToken token = default)
        where TEventHandler : class, IEventHandler;

    /// <summary>
    /// Reads stream and returns events.
    /// Conventions used:
    /// ProjectionCategoryStreamConvention - to construct streamId that shall be read.
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
    /// Conventions used:
    /// ProjectionCategoryStreamConvention - to construct streamId that shall be read.
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
    Task<IWriteResult> AppendState(object state, object id, long? version = null, CancellationToken token = default);

    /// <summary>
    /// Updates or adds simple entity/state. Be aware, that rdb constraints are not possible.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="state">The entity.</param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task<IWriteResult> AppendState<T>(T state, CancellationToken token = default);

    Task<SubscriptionRunnerState<T>?> GetState<T>(object id, string? streamId = null, CancellationToken token = default) where T:class;

    IAsyncEnumerable<(T, Metadata)> ReadEventsOfType<T>(string? streamId = null,
        StreamPosition? start = null, Direction? direction = null, long maxCount = 9223372036854775807L,
        CancellationToken token = default);

    Task<IAsyncDisposable> SubscribeStateEventHandler<TEventHandler>(
        IEnumerable<string>? eventTypes, 
        TEventHandler? eh = default,
        string? outputStream = null,
        FromRelativeStreamPosition? start = null, 
        bool ensureOutputStreamProjection = true, 
        CancellationToken token = default)
        where TEventHandler : class, IEventHandler, ITypeRegister;

    Task<IAsyncDisposable> SubscribeStateEventHandler<TEventHandler>(TEventHandler? eh = null,
        string? outputStream = null,
        FromRelativeStreamPosition? start = null, bool ensureOutputStreamProjection = true,
        CancellationToken token = default) where TEventHandler : class, IEventHandler, ITypeRegister;

    /// <summary>
    /// Ensures that a join projection is created or updated for the specified output stream and event types.
    /// </summary>
    /// <param name="outputStream">
    /// The name of the output stream where the projection results will be written.
    /// </param>
    /// <param name="eventTypes">
    /// A collection of event type names to be included in the join projection.
    /// </param>
    /// <param name="token">
    /// A <see cref="CancellationToken"/> to observe while waiting for the task to complete.
    /// </param>
    /// <returns>
    /// A <see cref="Task"/> representing the asynchronous operation.
    /// </returns>
    Task TryCreateJoinProjection(string outputStream, IEnumerable<string> eventTypes, CancellationToken token = default);

    /// <summary>
    /// Ensures that a join projection is created for the specified event handler type.
    /// </summary>
    /// <typeparam name="TEventHandler">
    /// The type of the event handler, which must implement both <see cref="IEventHandler"/> and <see cref="ITypeRegister"/>.
    /// </typeparam>
    /// <param name="outputStream">
    /// The name of the output stream for the join projection. If <c>null</c>, the output stream name is determined
    /// using the <see cref="IReadOnlyConventions.OutputStreamModelConvention"/> for the specified event handler type.
    /// </param>
    /// <param name="token">
    /// A <see cref="CancellationToken"/> to observe while waiting for the task to complete.
    /// </param>
    /// <returns>
    /// A <see cref="Task"/> representing the asynchronous operation.
    /// </returns>
    Task TryCreateJoinProjection<TEventHandler>(string? outputStream=null, CancellationToken token = default) where TEventHandler : class, IEventHandler, ITypeRegister;

    /// <summary>
    /// Appends metadata to a stream derived from the specified event type and identifier.
    /// </summary>
    /// <typeparam name="TEvent">The type of the event associated with the stream.</typeparam>
    /// <param name="id">The identifier of the event, used to derive the stream name.</param>
    /// <param name="state">The optional state of the stream (e.g., existing or new).</param>
    /// <param name="maxAge">The optional maximum age for events in the stream.</param>
    /// <param name="truncateBefore">The optional position before which events should be truncated.</param>
    /// <param name="cacheControl">The optional cache control duration for the stream.</param>
    /// <param name="acl">The optional access control list for the stream.</param>
    /// <param name="maxCount">The optional maximum number of events allowed in the stream.</param>
    /// <returns>A task that represents the asynchronous operation, containing the result of the write operation.</returns>
    /// <remarks>
    /// The stream name is derived using the <see cref="IReadOnlyConventions.StreamNameFromEventConvention"/> convention.
    /// </remarks>
    Task<IWriteResult> AppendStreamMetadataFromEvent<TEvent>(
        object id,
        StreamState? state = null,
        TimeSpan? maxAge = null,
        StreamPosition? truncateBefore = null,
        TimeSpan? cacheControl = null,
        StreamAcl? acl = null,
        int? maxCount = null);

    /// <summary>
    /// Appends metadata to a stream based on the specified handler type.
    /// </summary>
    /// <typeparam name="THandler">
    /// The type of the handler that determines the stream for which metadata will be appended.
    /// </typeparam>
    /// <param name="state">
    /// The <see cref="StreamState"/> indicating the expected state of the stream. 
    /// If <c>null</c>, the operation will not check the stream's state.
    /// </param>
    /// <param name="maxAge">
    /// The maximum age of events in the stream. Events older than this value will be removed.
    /// </param>
    /// <param name="truncateBefore">
    /// The <see cref="StreamPosition"/> before which events will be truncated.
    /// </param>
    /// <param name="cacheControl">
    /// The duration for which the stream metadata can be cached.
    /// </param>
    /// <param name="acl">
    /// The access control list (<see cref="StreamAcl"/>) specifying permissions for the stream.
    /// </param>
    /// <param name="maxCount">
    /// The maximum number of events allowed in the stream. Events exceeding this count will be removed.
    /// </param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains an <see cref="IWriteResult"/> 
    /// indicating the outcome of the operation.
    /// </returns>
    Task<IWriteResult> AppendStreamMetadataFromHandler<THandler>(
        StreamState? state = null,
        TimeSpan? maxAge = null,
        StreamPosition? truncateBefore = null,
        TimeSpan? cacheControl = null,
        StreamAcl? acl = null,
        int? maxCount = null);

    /// <summary>
    /// Appends metadata to a stream associated with a specified aggregate type and identifier.
    /// </summary>
    /// <typeparam name="TAggregate">
    /// The type of the aggregate associated with the stream.
    /// </typeparam>
    /// <param name="id">
    /// The identifier of the aggregate.
    /// </param>
    /// <param name="state">
    /// The optional state of the stream, such as <see cref="StreamState"/>.
    /// </param>
    /// <param name="maxAge">
    /// The optional maximum age for events in the stream.
    /// </param>
    /// <param name="truncateBefore">
    /// The optional position before which events in the stream should be truncated.
    /// </param>
    /// <param name="cacheControl">
    /// The optional cache control duration for the stream.
    /// </param>
    /// <param name="acl">
    /// The optional access control list (ACL) for the stream.
    /// </param>
    /// <param name="maxCount">
    /// The optional maximum number of events allowed in the stream.
    /// </param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains an <see cref="IWriteResult"/> 
    /// indicating the outcome of the operation.
    /// </returns>
    /// <remarks>
    /// This method uses the conventions defined in <see cref="IReadOnlyConventions"/> to determine the stream ID 
    /// based on the aggregate type and identifier.
    /// </remarks>
    Task<IWriteResult> AppendStreamMetadataFromAggregate<TAggregate>(
        object id,
        StreamState? state = null,
        TimeSpan? maxAge = null,
        StreamPosition? truncateBefore = null,
        TimeSpan? cacheControl = null,
        StreamAcl? acl = null,
        int? maxCount = null);

    /// <summary>
    /// Appends metadata to a specified stream in the EventStore.
    /// </summary>
    /// <param name="streamId">The identifier of the stream to which metadata will be appended.</param>
    /// <param name="state">
    /// The expected state of the stream. If <c>null</c>, defaults to <see cref="StreamState.Any"/>.
    /// </param>
    /// <param name="maxAge">
    /// The maximum age of events in the stream. Events older than this value will be removed.
    /// </param>
    /// <param name="truncateBefore">
    /// The position in the stream before which events will be truncated.
    /// </param>
    /// <param name="cacheControl">
    /// The duration for which the stream metadata can be cached.
    /// </param>
    /// <param name="acl">
    /// The access control list (ACL) defining permissions for the stream.
    /// </param>
    /// <param name="maxCount">
    /// The maximum number of events allowed in the stream. Events exceeding this count will be removed.
    /// </param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains the write result of the operation.
    /// </returns>
    Task<IWriteResult> AppendStreamMetadata(string streamId, StreamState? state, TimeSpan? maxAge, StreamPosition? truncateBefore,
        TimeSpan? cacheControl, StreamAcl? acl, int? maxCount);
}