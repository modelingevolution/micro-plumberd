using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using EventStore.Client;
using MicroPlumberd.Utils;

namespace MicroPlumberd;

/// <summary>
/// Delegate invoked just before an event is appended to EventStore.
/// Used as a fast delivery channel — the event is delivered to subscribers immediately,
/// before the EventStore write completes. EventStore is always written to regardless.
/// </summary>
/// <param name="context">The operation context.</param>
/// <param name="evt">The event being appended.</param>
/// <param name="id">The stream identifier (second segment of typical streamId).</param>
/// <param name="metadata">Optional metadata.</param>
public delegate Task EventAppendingHandler(OperationContext context, object evt, object? id, object? metadata);

/// <summary>
///     Root class for ED plumbing.
/// </summary>
public class PlumberEngine : IPlumberReadOnlyConfig
{
    private readonly ConcurrentDictionary<Type, object> _extension = new();
    private readonly ConcurrentDictionary<Type, ISnapshotPolicy> _policies = new();
    private readonly TypeHandlerRegisters _typeHandlerRegisters;
    private readonly ConcurrentDictionary<Type, IObjectSerializer> _serializers = new();

    /// <summary>
    /// Raised just before an event is appended to EventStore.
    /// Provides a fast in-process delivery channel — subscribers receive the event
    /// before the EventStore write begins. EventStore is always written to regardless.
    /// </summary>
    public event EventAppendingHandler? EventAppending;
    
    private readonly VersionDuckTyping _versionTyping = new();
    private readonly Func<Exception, OperationContext, CancellationToken, Task<ErrorHandleDecision>> _errorHandle;
    private ProjectionRegister? _projectionRegister;

    internal PlumberEngine(EventStoreClientSettings settings, PlumberConfig? config = null)
    {
        config ??= new PlumberConfig();
        Client = new EventStoreClient(settings);
        PersistentSubscriptionClient = new EventStorePersistentSubscriptionsClient(settings);
        ProjectionManagementClient = new EventStoreProjectionManagementClient(settings);
        Conventions = config.Conventions;
        MetadataFactory = new MetadataFactory(Conventions);
        SerializerFactory = config.SerializerFactory;
        ServiceProvider = config.ServiceProvider;
        _extension = config.Extension; // Shouldn't we make a copy?
        _typeHandlerRegisters = new TypeHandlerRegisters(Conventions.GetEventNameConvention);
        this._errorHandle = config.ErrorHandlePolicy;
        config.OnCreated(this);
    }
    /// <summary>
    /// Gets the read-only configuration for this plumber engine instance.
    /// </summary>
    public IPlumberReadOnlyConfig Config => this;

    /// <summary>
    /// Gets the type handler registers for mapping event types to their handlers.
    /// </summary>
    public ITypeHandlerRegisters TypeHandlerRegisters => _typeHandlerRegisters;

    /// <summary>
    /// Gets the EventStore client for interacting with EventStoreDB.
    /// </summary>
    public EventStoreClient Client { get; }

    /// <summary>
    /// Gets the projection register for managing EventStore projections.
    /// </summary>
    public IProjectionRegister ProjectionRegister =>
        _projectionRegister ??= new ProjectionRegister(ProjectionManagementClient);

    /// <summary>
    /// Gets the persistent subscription client for managing persistent subscriptions.
    /// </summary>
    public EventStorePersistentSubscriptionsClient PersistentSubscriptionClient { get; }

    /// <summary>
    /// Gets the projection management client for creating and managing EventStore projections.
    /// </summary>
    public EventStoreProjectionManagementClient ProjectionManagementClient { get; }

    /// <summary>
    /// Gets the service provider for dependency injection.
    /// </summary>
    public IServiceProvider ServiceProvider { get; }

    /// <inheritdoc/>
    public Task<ErrorHandleDecision> HandleError(Exception ex,OperationContext context, CancellationToken t) => _errorHandle(ex, context, t);

    /// <inheritdoc/>
    public Func<Type, IObjectSerializer> SerializerFactory { get; }

    /// <inheritdoc/>
    public IReadOnlyConventions Conventions { get; }

    /// <summary>
    /// Gets the metadata factory for creating <see cref="Metadata"/> instances with proper JSON schema.
    /// </summary>
    public MetadataFactory MetadataFactory { get; }

    /// <summary>
    /// Creates a subscription to a stream starting from the specified position.
    /// </summary>
    /// <param name="streamName">The name of the stream to subscribe to.</param>
    /// <param name="start">The starting position for the subscription.</param>
    /// <param name="userCredentials">Optional user credentials.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A subscription runner for handling events.</returns>
    public ISubscriptionRunner Subscribe(string streamName, FromRelativeStreamPosition start,
        UserCredentials? userCredentials = null, CancellationToken cancellationToken = default)
    {
        if (start.Count == 0)
            return new SubscriptionRunner(this,
                new SubscriptionRunnerState(start.StartPosition, Client, streamName, userCredentials, cancellationToken));
        return new SubscriptionSeeker(this, streamName, start, userCredentials, cancellationToken);
    }

    /// <summary>
    /// Subscribes an event handler to a stream with custom event type mapping.
    /// </summary>
    /// <typeparam name="TEventHandler">The type of event handler.</typeparam>
    /// <param name="mapFunc">Function to map event names to types.</param>
    /// <param name="eventTypes">Collection of event type names to subscribe to.</param>
    /// <param name="eh">Optional event handler instance.</param>
    /// <param name="outputStream">Optional output stream name.</param>
    /// <param name="start">Optional starting position.</param>
    /// <param name="ensureOutputStreamProjection">Whether to ensure the output stream projection exists.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A disposable subscription.</returns>
    public Task<IAsyncDisposable> SubscribeEventHandler<TEventHandler>(TypeEventConverter mapFunc,
        IEnumerable<string>? eventTypes,
        TEventHandler? eh = null, string? outputStream = null, FromStream? start = null,
        bool ensureOutputStreamProjection = true, CancellationToken ct = default) where TEventHandler : class, IEventHandler
    {
        return SubscribeEventHandler<TEventHandler>(mapFunc, eventTypes, eh, outputStream,
            start != null ? (FromRelativeStreamPosition)start.Value : null, ensureOutputStreamProjection, ct);
    }

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
    public Task TryCreateJoinProjection<TEventHandler>(string? outputStream=null, CancellationToken token = default) where TEventHandler : class, IEventHandler, ITypeRegister
    {
        return TryCreateJoinProjection(outputStream ?? Conventions.OutputStreamModelConvention(typeof(TEventHandler)), _typeHandlerRegisters.GetEventNamesFor<TEventHandler>(), token);
    }

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
    public async Task TryCreateJoinProjection(string outputStream, IEnumerable<string> eventTypes, CancellationToken token = default)
    {
        await ProjectionManagementClient.TryCreateJoinProjection(outputStream, ProjectionRegister, eventTypes, token: token);
    }
    /// <summary>
    /// Subscribes an event handler to a stream using auto-discovered event types.
    /// </summary>
    /// <typeparam name="TEventHandler">The type of event handler that implements both IEventHandler and ITypeRegister.</typeparam>
    /// <param name="eh">Optional event handler instance. If null, a new instance will be created from the service provider.</param>
    /// <param name="outputStream">Optional output stream name. If null, determined by naming conventions.</param>
    /// <param name="start">Optional starting position for reading events. If null, starts from the beginning.</param>
    /// <param name="ensureOutputStreamProjection">Whether to ensure the output stream projection exists before subscribing.</param>
    /// <param name="token">Cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>A disposable subscription that can be disposed to stop processing events.</returns>
    public async Task<IAsyncDisposable> SubscribeEventHandler<TEventHandler>(TEventHandler? eh = null,
        string? outputStream = null,
        FromRelativeStreamPosition? start = null, bool ensureOutputStreamProjection = true,
        CancellationToken token = default) where TEventHandler : class, IEventHandler, ITypeRegister
    {
        return await SubscribeEventHandler(_typeHandlerRegisters.GetEventNameConverterFor<TEventHandler>()!,
            _typeHandlerRegisters.GetEventNamesFor<TEventHandler>(),
            eh, outputStream, start, ensureOutputStreamProjection, token);
    }

    /// <summary>
    /// Subscribes an event handler to state snapshot events.
    /// </summary>
    /// <typeparam name="TEventHandler">The type of event handler.</typeparam>
    /// <param name="eh">Optional event handler instance.</param>
    /// <param name="outputStream">Optional output stream name.</param>
    /// <param name="start">Optional starting position.</param>
    /// <param name="ensureOutputStreamProjection">Whether to ensure the output stream projection exists.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>A disposable subscription.</returns>
    public async Task<IAsyncDisposable> SubscribeStateEventHandler<TEventHandler>(TEventHandler? eh = null,
        string? outputStream = null,
        FromRelativeStreamPosition? start = null, bool ensureOutputStreamProjection = true,
        CancellationToken token = default) where TEventHandler : class, IEventHandler, ITypeRegister
    {
        var evts = TEventHandler.Types.Select(x => this.Conventions.SnapshotEventNameConvention(x)).ToArray();
        return await SubscribeStateEventHandler(evts, eh, outputStream, start, ensureOutputStreamProjection, token);
    }
    /// <summary>
    /// Subscribes an event handler to state snapshot events with custom event type filtering.
    /// </summary>
    /// <typeparam name="TEventHandler">The type of event handler that implements both IEventHandler and ITypeRegister.</typeparam>
    /// <param name="eventTypes">Collection of event type names to subscribe to. If null, subscribes to all types from TEventHandler.</param>
    /// <param name="eh">Optional event handler instance. If null, a new instance will be created from the service provider.</param>
    /// <param name="outputStream">Optional output stream name. If null, determined by naming conventions.</param>
    /// <param name="start">Optional starting position for reading events. If null, starts from the beginning.</param>
    /// <param name="ensureOutputStreamProjection">Whether to ensure the output stream projection exists before subscribing.</param>
    /// <param name="token">Cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>A disposable subscription that can be disposed to stop processing events.</returns>
    public async Task<IAsyncDisposable> SubscribeStateEventHandler<TEventHandler>(
        IEnumerable<string>? eventTypes,
        TEventHandler? eh = default,
        string? outputStream = null,
        FromRelativeStreamPosition? start = null,
        bool ensureOutputStreamProjection = true,
        CancellationToken token = default)
        where TEventHandler : class, IEventHandler, ITypeRegister
    {
        eventTypes ??= Array.Empty<string>();

        outputStream ??= Conventions.OutputStreamModelConvention(typeof(TEventHandler));
        if (ensureOutputStreamProjection)
            await ProjectionManagementClient.TryCreateJoinProjection(outputStream, ProjectionRegister, eventTypes, token: token);
        var sub = Subscribe(outputStream, start ?? FromStream.Start, cancellationToken: token);
        if (eh == null)
            await sub.WithSnapshotHandler<TEventHandler>();
        else
            await sub.WithSnapshotHandler(eh);
        return sub;
    }

    /// <summary>
    /// Subscribes an event handler to a stream with custom event type mapping.
    /// </summary>
    /// <typeparam name="TEventHandler">The type of event handler.</typeparam>
    /// <param name="mapFunc">Function to map event names to types.</param>
    /// <param name="eventTypes">Collection of event type names to subscribe to.</param>
    /// <param name="eh">Optional event handler instance.</param>
    /// <param name="outputStream">Optional output stream name.</param>
    /// <param name="start">Optional starting position.</param>
    /// <param name="ensureOutputStreamProjection">Whether to ensure the output stream projection exists.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>A disposable subscription.</returns>
    public async Task<IAsyncDisposable> SubscribeEventHandler<TEventHandler>(TypeEventConverter mapFunc,
        IEnumerable<string>? eventTypes, TEventHandler? eh = default, string? outputStream = null,
        FromRelativeStreamPosition? start = null, bool ensureOutputStreamProjection = true, CancellationToken token = default)
        where TEventHandler : class, IEventHandler
    {
        eventTypes ??= Array.Empty<string>();

        outputStream ??= Conventions.OutputStreamModelConvention(typeof(TEventHandler));
        if (ensureOutputStreamProjection)
            await ProjectionManagementClient.TryCreateJoinProjection(outputStream, ProjectionRegister, eventTypes, token: token);
        var sub = Subscribe(outputStream, start ?? FromStream.Start, cancellationToken:token);
        if (eh == null)
            await sub.WithHandler<TEventHandler>(mapFunc);
        else
            await sub.WithHandler(eh, mapFunc);
        return sub;
    }

    /// <summary>
    /// Subscribes an event handler persistently with at-least-once delivery semantics.
    /// </summary>
    /// <typeparam name="TEventHandler">The type of event handler.</typeparam>
    /// <param name="mapFunc">Function to map event names to types.</param>
    /// <param name="events">Collection of event type names to subscribe to.</param>
    /// <param name="model">Optional event handler instance.</param>
    /// <param name="outputStream">Optional output stream name.</param>
    /// <param name="groupName">Optional persistent subscription group name.</param>
    /// <param name="startFrom">Optional starting position.</param>
    /// <param name="ensureOutputStreamProjection">Whether to ensure the output stream projection exists.</param>
    /// <param name="minCheckpointCount">Minimum checkpoint count for the persistent subscription.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>A disposable subscription.</returns>
    public async Task<IAsyncDisposable> SubscribeEventHandlerPersistently<TEventHandler>(TypeEventConverter mapFunc,
        IEnumerable<string>? events,
        TEventHandler? model,
        string? outputStream = null, string? groupName = null, IPosition? startFrom = null,
        bool ensureOutputStreamProjection = true,
        int minCheckpointCount = 1,
        CancellationToken token = default)
        where TEventHandler : class, IEventHandler
    {
        var handlerType = typeof(TEventHandler);
        startFrom ??= StreamPosition.End;
        outputStream ??= Conventions.OutputStreamModelConvention(handlerType);
        groupName ??= Conventions.GroupNameModelConvention(handlerType);
        if (ensureOutputStreamProjection)
            await ProjectionManagementClient.TryCreateJoinProjection(outputStream, ProjectionRegister, events, token);

        try
        {
            await PersistentSubscriptionClient.GetInfoToStreamAsync(outputStream, groupName, cancellationToken: token);
        }
        catch (PersistentSubscriptionNotFoundException)
        {
            await PersistentSubscriptionClient.CreateToStreamAsync(outputStream, groupName,
                new PersistentSubscriptionSettings(true, startFrom, checkPointLowerBound: minCheckpointCount), cancellationToken: token);
        }

        var sub = SubscribePersistently(outputStream, groupName, cancellationToken:token);
        if (model == null)
            await sub.WithHandler<TEventHandler>(mapFunc);
        else
            await sub.WithHandler(model, mapFunc);
        return sub;
        return sub;
    }

    /// <summary>
    /// Subscribes an event handler persistently with at-least-once delivery semantics.
    /// </summary>
    /// <typeparam name="TEventHandler">The type of event handler.</typeparam>
    /// <param name="model">Optional event handler instance.</param>
    /// <param name="outputStream">Optional output stream name.</param>
    /// <param name="groupName">Optional persistent subscription group name.</param>
    /// <param name="startFrom">Optional starting position.</param>
    /// <param name="ensureOutputStreamProjection">Whether to ensure the output stream projection exists.</param>
    /// <param name="minCheckPointCount">Minimum checkpoint count for the persistent subscription.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>A disposable subscription.</returns>
    public Task<IAsyncDisposable> SubscribeEventHandlerPersistently<TEventHandler>(TEventHandler? model=null,
        string? outputStream = null, string? groupName = null, IPosition? startFrom = null,
        bool ensureOutputStreamProjection = true, int minCheckPointCount = 1, CancellationToken token = default)
        where TEventHandler : class, IEventHandler, ITypeRegister
    {
        return SubscribeEventHandlerPersistently(_typeHandlerRegisters.GetEventNameConverterFor<TEventHandler>(),
            _typeHandlerRegisters.GetEventNamesFor<TEventHandler>(),
            model, outputStream, groupName, startFrom, ensureOutputStreamProjection, minCheckPointCount, token);
    }

    /// <summary>
    /// Creates a persistent subscription to a stream with at-least-once delivery semantics.
    /// </summary>
    /// <param name="streamName">The name of the stream to subscribe to.</param>
    /// <param name="groupName">The persistent subscription group name.</param>
    /// <param name="bufferSize">The buffer size for the subscription.</param>
    /// <param name="userCredentials">Optional user credentials.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A subscription runner for handling events.</returns>
    public ISubscriptionRunner SubscribePersistently(string streamName, string groupName, int bufferSize = 10,
        UserCredentials? userCredentials = null, CancellationToken cancellationToken = default)
    {
        return new PersistentSubscriptionRunner(this,
            PersistentSubscriptionClient.SubscribeToStream(streamName, groupName, bufferSize,  userCredentials,
                cancellationToken));
    }

    /// <summary>
    /// Rehydrates a model by replaying events from a stream identified by an aggregate ID.
    /// </summary>
    /// <typeparam name="T">The type of model to rehydrate.</typeparam>
    /// <param name="context">The operation context.</param>
    /// <param name="model">The model instance to rehydrate.</param>
    /// <param name="id">The aggregate ID.</param>
    /// <param name="position">Optional starting position in the stream.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task Rehydrate<T>(OperationContext context, T model,  Guid id, StreamPosition? position = null, CancellationToken token = default)
        where T : IEventHandler, ITypeRegister
    {
        var streamId = Conventions.GetStreamIdConvention(context,typeof(T), id);
        await Rehydrate(context,model, streamId, position, token);
    }

    /// <summary>
    /// Rehydrates a model by replaying events from the specified stream.
    /// </summary>
    /// <typeparam name="T">The type of model to rehydrate.</typeparam>
    /// <param name="context">The operation context.</param>
    /// <param name="model">The model instance to rehydrate.</param>
    /// <param name="streamId">The stream ID.</param>
    /// <param name="position">Optional starting position in the stream.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task Rehydrate<T>(OperationContext context, T model, string streamId, StreamPosition? position = null, CancellationToken token = default)
        where T : IEventHandler, ITypeRegister
    {
        TypeEventConverter registry = _typeHandlerRegisters.GetEventNameConverterFor<T>();
        await Rehydrate(context,model, streamId, registry, position, token);
    }

    /// <summary>
    /// Rehydrates a model by replaying events from the specified stream using a custom type converter.
    /// </summary>
    /// <typeparam name="T">The type of model to rehydrate.</typeparam>
    /// <param name="context">The operation context.</param>
    /// <param name="model">The model instance to rehydrate.</param>
    /// <param name="streamId">The stream ID.</param>
    /// <param name="converter">Function to convert event names to types.</param>
    /// <param name="position">Optional starting position in the stream.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task Rehydrate<T>(OperationContext context, T model, string streamId, TypeEventConverter converter, StreamPosition? position = null, CancellationToken token = default)
        where T : IEventHandler
    {
        var pos = position ?? StreamPosition.Start;
        var items = Client.ReadStreamAsync(Direction.Forwards, streamId, pos, resolveLinkTos: true, cancellationToken: token);

        var vAware = model as IVersionAware;

        if (await items.ReadState == ReadState.StreamNotFound) return;

        await foreach (var i in items)
        {
            if (!converter(i.Event.EventType, out var t))
                continue;
            var (evt, metadata) = ReadEventData(context,i.Event, i.Link, t);
            await model.Handle(metadata, evt);
            vAware?.Increase();
        }
    }

    /// <summary>
    /// Finds an event in a stream by its event ID.
    /// </summary>
    /// <param name="context">The operation context.</param>
    /// <param name="streamId">The stream ID to search.</param>
    /// <param name="id">The event ID to find.</param>
    /// <param name="eventMapping">Function to map event names to types.</param>
    /// <param name="scanDirection">The direction to scan the stream.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>The event record if found; otherwise, null.</returns>
    public async Task<IEventRecord?> FindEventInStream(OperationContext context, string streamId, Guid id,
        TypeEventConverter eventMapping, Direction scanDirection = Direction.Backwards, CancellationToken token = default)
    {
        return await FindEventInStream<object>(context,streamId, id, eventMapping, scanDirection, token);
    }

    /// <summary>
    /// Finds a typed event in a stream by its event ID.
    /// </summary>
    /// <typeparam name="TEvent">The type of event to find.</typeparam>
    /// <param name="context">The operation context.</param>
    /// <param name="streamId">The stream ID to search.</param>
    /// <param name="id">The event ID to find.</param>
    /// <param name="eventMapping">Optional function to map event names to types.</param>
    /// <param name="scanDirection">The direction to scan the stream.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>The typed event record if found; otherwise, null.</returns>
    public async Task<IEventRecord<TEvent>?> FindEventInStream<TEvent>(OperationContext context, string streamId, Guid id,
        TypeEventConverter? eventMapping = null, Direction scanDirection = Direction.Backwards, CancellationToken token = default)
    {
        var items = Client.ReadStreamAsync(Direction.Forwards, streamId, StreamPosition.Start, cancellationToken:token);

        bool TryMapEventByName(string x, out Type tt)
        {
            var name = Conventions.GetEventNameConvention(null, typeof(TEvent));
            if (x == name)
            {
                tt = typeof(TEvent);
                return true;
            }

            tt = null;
            return false;
        }

        eventMapping ??= TryMapEventByName;
        await foreach (var i in items)
        {
            if (i.Event.EventId.ToGuid() != id) continue;
            if (eventMapping(i.Event.EventType, out var t))
                throw new ArgumentException($"We don't know how to deserialize this event: {i.Event.EventType}.");

            var (evt, metadata) = ReadEventData(context,i.Event,i.Link, t);
            return new EventRecord<TEvent> { Event = (TEvent)evt, Metadata = metadata };
        }

        return null;
    }

    /// <summary>
    /// Creates a subscription set for composing multiple event handlers.
    /// </summary>
    /// <returns>A subscription set builder.</returns>
    public IEngineSubscriptionSet SubscribeSet()
    {
        return new SubscriptionSet(this);
    }

    /// <summary>
    /// Reads events from a stream identified by an aggregate ID and owner type.
    /// </summary>
    /// <typeparam name="TOwner">The type that owns the stream and implements ITypeRegister.</typeparam>
    /// <param name="context">The operation context for the read operation.</param>
    /// <param name="id">The aggregate ID identifying the specific stream.</param>
    /// <param name="start">Optional starting position in the stream. Defaults to the beginning.</param>
    /// <param name="direction">Optional direction to read (forwards or backwards). Defaults to forwards.</param>
    /// <param name="maxCount">Maximum number of events to read. Defaults to all events.</param>
    /// <param name="token">Cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>An async enumerable of deserialized events.</returns>
    public IAsyncEnumerable<object> Read<TOwner>(OperationContext context, object id, StreamPosition? start = null, Direction? direction = null,
        long maxCount = long.MaxValue, CancellationToken token = default) where TOwner : ITypeRegister
    {
        start ??= StreamPosition.Start;

        var streamId = Conventions.GetStreamIdConvention(context,typeof(TOwner), id);
        var registry = _typeHandlerRegisters.GetEventNameConverterFor<TOwner>();
        return Read(context,streamId, registry, start, direction, maxCount, token);
    }

    /// <summary>
    /// Reads events from a category stream for the specified owner type.
    /// </summary>
    /// <typeparam name="TOwner">The type that owns the category stream and implements ITypeRegister.</typeparam>
    /// <param name="context">The operation context for the read operation.</param>
    /// <param name="start">Optional starting position in the stream. Defaults to the beginning.</param>
    /// <param name="direction">Optional direction to read (forwards or backwards). Defaults to forwards.</param>
    /// <param name="maxCount">Maximum number of events to read. Defaults to all events.</param>
    /// <param name="token">Cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>An async enumerable of deserialized events.</returns>
    public IAsyncEnumerable<object> Read<TOwner>(OperationContext context, StreamPosition? start = null, Direction? direction = null,
        long maxCount = long.MaxValue, CancellationToken token = default) where TOwner : ITypeRegister
    {
        var streamId = Conventions.ProjectionCategoryStreamConvention(typeof(TOwner));
        var evNameConv = _typeHandlerRegisters.GetEventNameConverterFor<TOwner>();
        return Read(context, streamId, evNameConv, start, direction, maxCount, token);
    }

    /// <summary>
    /// Reads events from a stream including their metadata.
    /// </summary>
    /// <param name="context">The operation context for the read operation.</param>
    /// <param name="streamId">The stream ID to read from.</param>
    /// <param name="converter">Function to convert event names to types.</param>
    /// <param name="start">Optional starting position in the stream. Defaults to the beginning.</param>
    /// <param name="direction">Optional direction to read (forwards or backwards). Defaults to forwards.</param>
    /// <param name="maxCount">Maximum number of events to read. Defaults to all events.</param>
    /// <param name="token">Cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>An async enumerable of tuples containing the event and its metadata.</returns>
    public async IAsyncEnumerable<(object, Metadata)> ReadFull(OperationContext context, string streamId, TypeEventConverter converter,
        StreamPosition? start = null, Direction? direction = null, long maxCount = long.MaxValue, [EnumeratorCancellation] CancellationToken token = default)
    {
        var d = direction ?? Direction.Forwards;
        var p = start ?? StreamPosition.Start;

        var items = Client.ReadStreamAsync(d, streamId, p, resolveLinkTos: true, maxCount: maxCount, cancellationToken:token);
        if (await items.ReadState == ReadState.StreamNotFound) yield break;

        var events = items.Select(x => new
                { ResolvedEvent = x, EventType = converter(x.Event.EventType, out var t) ? t : null })
            .Where(x => x.EventType != null)
            .Select(ev => ReadEventData(context,ev.ResolvedEvent.Event, ev.ResolvedEvent.Link, ev.EventType));
        await foreach (var i in events)
            yield return i;
    }

    /// <summary>
    /// Reads events of a specific type from a stream.
    /// </summary>
    /// <typeparam name="T">The type of events to read.</typeparam>
    /// <param name="context">The operation context for the read operation.</param>
    /// <param name="streamId">Optional stream ID. If null, reads from the event type stream ($et-{eventName}).</param>
    /// <param name="start">Optional starting position in the stream. Defaults to the beginning.</param>
    /// <param name="direction">Optional direction to read (forwards or backwards). Defaults to forwards.</param>
    /// <param name="maxCount">Maximum number of events to read. Defaults to all events.</param>
    /// <param name="token">Cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>An async enumerable of tuples containing the typed event and its metadata.</returns>
    public async IAsyncEnumerable<(T, Metadata)> ReadEventsOfType<T>(OperationContext context, string? streamId = null, StreamPosition? start = null, Direction? direction = null,
        long maxCount = 9223372036854775807, CancellationToken token = default)
    {
        string eventName = this.Conventions.GetEventNameConvention(null, typeof(T));

        bool converter(string en, out Type t)
        {
            if (en == eventName)
            {
                t = typeof(T);
                return true;
            }

            t = null;
            return false;
        }

        streamId ??= $"$et-{eventName}";

        var d = direction ?? Direction.Forwards;
        var p = start ?? StreamPosition.Start;

        var items = Client.ReadStreamAsync(d, streamId, p, resolveLinkTos: true, maxCount: maxCount, cancellationToken: token);
        if (await items.ReadState == ReadState.StreamNotFound) yield break;

        var events = items.Select(x => new
                { ResolvedEvent = x, EventType = converter(x.Event.EventType, out var t) ? t : null })
            .Where(x => x.EventType != null)
            .Select(ev => ReadEventData(context,ev.ResolvedEvent.Event, ev.ResolvedEvent.Link, ev.EventType));
        await foreach (var i in events)
            yield return ((T)i.Item1, i.Item2);
    }

    /// <summary>
    /// Reads events from a stream using a custom type converter.
    /// </summary>
    /// <param name="context">The operation context for the read operation.</param>
    /// <param name="streamId">The stream ID to read from.</param>
    /// <param name="converter">Function to convert event names to types.</param>
    /// <param name="start">Optional starting position in the stream. Defaults to the beginning.</param>
    /// <param name="direction">Optional direction to read (forwards or backwards). Defaults to forwards.</param>
    /// <param name="maxCount">Maximum number of events to read. Defaults to all events.</param>
    /// <param name="token">Cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>An async enumerable of deserialized events.</returns>
    public async IAsyncEnumerable<object> Read(OperationContext context, string streamId, TypeEventConverter converter,
        StreamPosition? start = null, Direction? direction = null, long maxCount = long.MaxValue,[EnumeratorCancellation] CancellationToken token = default)
    {
        var d = direction ?? Direction.Forwards;
        var p = start ?? StreamPosition.Start;

        var items = Client.ReadStreamAsync(d, streamId, p, resolveLinkTos: true, maxCount: maxCount, cancellationToken: token);
        if (await items.ReadState == ReadState.StreamNotFound) yield break;

        var events = items.Select(x => new
                { ResolvedEvent = x, EventType = converter(x.Event.EventType, out var t) ? t : null })
            .Where(x => x.EventType != null)
            .Select(ev => Serializer(ev.EventType).Deserialize(context,ev.ResolvedEvent.Event.Data.Span, ev.EventType!));
        await foreach (var i in events)
            yield return i;
    }

    /// <summary>
    /// Gets and rehydrates an aggregate from its event stream, optionally using a snapshot for performance.
    /// </summary>
    /// <typeparam name="TOwner">The type of aggregate to retrieve.</typeparam>
    /// <param name="context">The operation context for the get operation.</param>
    /// <param name="id">The aggregate ID.</param>
    /// <param name="token">Cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>The fully rehydrated aggregate.</returns>
    public async Task<TOwner> Get<TOwner>(OperationContext context, object id, CancellationToken token = default)
        where TOwner : IAggregate<TOwner>, ITypeRegister, IId
    {
        var sp = StreamPosition.Start;
        var aggregate = TOwner.Empty(id);
        if (GetPolicy<TOwner>() != null && aggregate is IStatefull i)
        {
            var snapshot = await GetSnapshot(context,id, i.SnapshotType, token);
            if (snapshot != null)
            {
                i.Initialize(snapshot.Value, new StateInfo(snapshot.Version, snapshot.Created));
                sp = StreamPosition.FromInt64(snapshot.Version + 1);
            }
        }

        await aggregate.Rehydrate(Read<TOwner>(context,id, sp, token: token));
        return aggregate;
    }

    /// <summary>
    /// Appends a collection of events to a stream with optimistic concurrency control using stream revision.
    /// </summary>
    /// <param name="context">The operation context for the append operation.</param>
    /// <param name="streamId">The stream ID to append to.</param>
    /// <param name="rev">The expected stream revision for optimistic concurrency control.</param>
    /// <param name="events">Collection of events to append.</param>
    /// <param name="metadata">Optional metadata to attach to all events.</param>
    /// <param name="token">Cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>The write result indicating success or failure.</returns>
    public async Task<IWriteResult> AppendEvents(OperationContext context, string streamId, StreamRevision rev, IEnumerable<object> events,
        object? metadata = null, CancellationToken token = default)
    {
        var evData = MakeEvents(context,events, metadata);

        return await Client.AppendToStreamAsync(streamId, rev, evData, cancellationToken: token);
    }

    /// <summary>
    /// Appends a collection of events to a stream with optimistic concurrency control using stream state.
    /// </summary>
    /// <param name="context">The operation context for the append operation.</param>
    /// <param name="streamId">The stream ID to append to.</param>
    /// <param name="state">The expected stream state (e.g., Any, NoStream, StreamExists).</param>
    /// <param name="events">Collection of events to append.</param>
    /// <param name="metadata">Optional metadata to attach to all events.</param>
    /// <param name="token">Cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>The write result indicating success or failure.</returns>
    public async Task<IWriteResult> AppendEvents(OperationContext context, string streamId, StreamState state, IEnumerable<object> events,
        object? metadata = null, CancellationToken token = default)
    {
        var evData = MakeEvents(context,events, metadata);

        var r = await Client.AppendToStreamAsync(streamId, state, evData, cancellationToken: token);
        return r;
    }

    /// <summary>
    /// Appends a state snapshot to its designated stream, extracting ID and version from the state object.
    /// </summary>
    /// <typeparam name="T">The type of state to append.</typeparam>
    /// <param name="context">The operation context for the append operation.</param>
    /// <param name="state">The state object to append. Must have ID and version properties.</param>
    /// <param name="token">Cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>The write result indicating success or failure.</returns>
    public Task<IWriteResult> AppendState<T>(OperationContext context, T state, CancellationToken token = default) =>
        AppendState(context,state, IdDuckTyping.Instance.GetId(state), _versionTyping.GetVersion(state), token);

    /// <summary>
    /// Appends a state snapshot to its designated stream with explicit ID and version.
    /// </summary>
    /// <param name="context">The operation context for the append operation.</param>
    /// <param name="state">The state object to append.</param>
    /// <param name="id">The ID identifying the state stream.</param>
    /// <param name="version">Optional version for optimistic concurrency. If null or negative, uses stream state.</param>
    /// <param name="token">Cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>The write result indicating success or failure.</returns>
    public async Task<IWriteResult> AppendState(OperationContext context, object state, object id, long? version, CancellationToken token = default)
    {
        var m = Conventions.GetMetadata(context,null, state, null);
        var stateType = state.GetType();
        var streamId = Conventions.GetStreamIdStateConvention(context, stateType, id);
        var evId = Conventions.GetEventIdStateConvention(state, id, version);
        var evData = MakeEvent(context, evId, Conventions.SnapshotEventNameConvention(stateType), state, m);
        var ret = (version == null || version < 0) ?
            await Client.AppendToStreamAsync(streamId, version == -1 ? StreamState.NoStream : StreamState.Any, [evData], cancellationToken: token) :
            await Client.AppendToStreamAsync(streamId, StreamRevision.FromInt64(version.Value), [evData], cancellationToken: token);
        _versionTyping.SetVersion(state, (version??-1) + 1);
        return ret;
    }

    /// <summary>
    /// Appends a snapshot of an aggregate's state to its snapshot stream.
    /// </summary>
    /// <param name="context">The operation context for the append operation.</param>
    /// <param name="snapshot">The snapshot object to append.</param>
    /// <param name="id">The aggregate ID.</param>
    /// <param name="version">The aggregate version at the time of the snapshot.</param>
    /// <param name="state">Optional expected stream state. Defaults to Any.</param>
    /// <param name="token">Cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>The write result indicating success or failure.</returns>
    public async Task<IWriteResult> AppendSnapshot(OperationContext context, object snapshot, object id, long version, StreamState? state = null, CancellationToken token = default)
    {
        var m = Conventions.GetMetadata(context, null, snapshot, new { SnapshotVersion = version });
        var stateType = snapshot.GetType();
        var streamId = Conventions.GetStreamIdSnapshotConvention(context, stateType, id);
        var evId = Conventions.GetEventIdConvention(context, null, snapshot);
        var evData = MakeEvent(context, evId, Conventions.SnapshotEventNameConvention(stateType), snapshot, m);

        return await Client.AppendToStreamAsync(streamId, state ?? StreamState.Any, [evData], cancellationToken:token);

    }

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
    public async Task<IWriteResult> AppendStreamMetadataFromEvent<TEvent>(OperationContext context,
        object id,
        StreamState? state = null,
        TimeSpan? maxAge = null,
        StreamPosition? truncateBefore = null,
        TimeSpan? cacheControl = null,
        StreamAcl? acl = null,
        int? maxCount = null)
    {
        var streamId = Conventions.StreamNameFromEventConvention(context,typeof(TEvent), id);
        // Create StreamMetadata with the provided arguments
        return await AppendStreamMetadata(context,streamId, state, maxAge, truncateBefore, cacheControl, acl, maxCount);
    }
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
    public async Task<IWriteResult> AppendStreamMetadataFromHandler<THandler>(
        OperationContext context,
        StreamState? state = null,
        TimeSpan? maxAge = null,
        StreamPosition? truncateBefore = null,
        TimeSpan? cacheControl = null,
        StreamAcl? acl = null,
        int? maxCount = null)
    {
        var streamId = Conventions.OutputStreamModelConvention(typeof(THandler));
        // Create StreamMetadata with the provided arguments
        return await AppendStreamMetadata(context,streamId, state, maxAge, truncateBefore, cacheControl, acl, maxCount);
    }
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
    public async Task<IWriteResult> AppendStreamMetadataFromAggregate<TAggregate>(
        OperationContext context,
        object id,
        StreamState? state = null,
        TimeSpan? maxAge = null,
        StreamPosition? truncateBefore = null,
        TimeSpan? cacheControl = null,
        StreamAcl? acl = null,
        int? maxCount = null)
    {
        var streamId = Conventions.GetStreamIdConvention(context, typeof(TAggregate), id);
        // Create StreamMetadata with the provided arguments
        return await AppendStreamMetadata(context,streamId, state, maxAge, truncateBefore, cacheControl, acl, maxCount);
    }
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
    public async Task<IWriteResult> AppendStreamMetadata(OperationContext context, string streamId, StreamState? state, TimeSpan? maxAge, StreamPosition? truncateBefore,
        TimeSpan? cacheControl, StreamAcl? acl, int? maxCount)
    {
        var metadata = new EventStore.Client.StreamMetadata(
            maxAge: maxAge,
            truncateBefore: truncateBefore,
            cacheControl: cacheControl,
            acl: acl,
            maxCount: maxCount
        );
        // Set the stream metadata
        return await Client.SetStreamMetadataAsync(streamId, state ?? StreamState.Any, metadata);
    }
    /// <summary>
    /// Appends a single event to a stream determined by naming conventions.
    /// </summary>
    /// <param name="context">The operation context for the append operation.</param>
    /// <param name="evt">The event to append. Cannot be null.</param>
    /// <param name="id">Optional identifier for the stream. If not provided, conventions will be used.</param>
    /// <param name="metadata">Optional metadata to attach to the event.</param>
    /// <param name="state">Optional expected stream state. Defaults to Any.</param>
    /// <param name="evtName">Optional event name. If not provided, determined by naming conventions.</param>
    /// <param name="token">Cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>The write result indicating success or failure.</returns>
    /// <exception cref="ArgumentException">Thrown when evt is null.</exception>
    public async Task<IWriteResult> AppendEvent(OperationContext context, object evt, object? id=null, object? metadata = null, StreamState? state=null, string? evtName=null, CancellationToken token = default)
    {
        if (evt == null) throw new ArgumentException("evt cannot be null.");

        if (EventAppending != null)
            await EventAppending(context, evt, id, metadata);

        evtName ??= Conventions.GetEventNameConvention(null, evt.GetType());
        var m = Conventions.GetMetadata(context,null, evt, metadata);
        var st = state ?? StreamState.Any;
        var streamId = Conventions.StreamNameFromEventConvention(context,evt.GetType(), id);
        var evId = Conventions.GetEventIdConvention(context,null, evt);
        var evData = MakeEvent(context,evId, evtName, evt, m);

        var r = await Client.AppendToStreamAsync(streamId, st, [evData], cancellationToken:token);
        return r;
    }

    /// <summary>
    /// Appends a single event to a specific stream.
    /// </summary>
    /// <param name="context">The operation context for the append operation.</param>
    /// <param name="streamId">The stream ID to append to. Cannot be null or empty.</param>
    /// <param name="evt">The event to append. Cannot be null.</param>
    /// <param name="state">Optional expected stream state. Defaults to Any.</param>
    /// <param name="evtName">Optional event name. If not provided, determined by naming conventions.</param>
    /// <param name="metadata">Optional metadata to attach to the event.</param>
    /// <param name="token">Cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>The write result indicating success or failure.</returns>
    /// <exception cref="ArgumentException">Thrown when streamId is null or empty, or when evt is null.</exception>
    public async Task<IWriteResult> AppendEventToStream(OperationContext context, string streamId, object evt, StreamState? state = null, string? evtName = null,
        object? metadata = null, CancellationToken token = default)
    {
        if (string.IsNullOrEmpty(streamId)) throw new ArgumentException("steamId cannot be null or empty.");
        if (evt == null) throw new ArgumentException("Event cannot be null");

        if (EventAppending != null)
            await EventAppending(context, evt, streamId, metadata);

        StreamState st = state ?? StreamState.Any;
        var eventName = evtName ?? Conventions.GetEventNameConvention( null, evt.GetType());
        var m = Conventions.GetMetadata(context, null, evt, metadata);
        var evId = Conventions.GetEventIdConvention(context, null, evt);
        var evData = MakeEvent(context,evId, eventName, evt, m);

        var r = await Client.AppendToStreamAsync(streamId, st, [evData], cancellationToken: token);
        return r;
    }

    /// <summary>
    /// Saves changes to an existing aggregate by appending its pending events to the stream.
    /// </summary>
    /// <typeparam name="T">The type of aggregate.</typeparam>
    /// <param name="context">The operation context for the save operation.</param>
    /// <param name="aggregate">The aggregate with pending events to save. Cannot be null.</param>
    /// <param name="metadata">Optional metadata to attach to all events.</param>
    /// <param name="streamMetadata">Optional stream metadata configuration.</param>
    /// <param name="token">Cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>The write result indicating success or failure.</returns>
    /// <exception cref="ArgumentNullException">Thrown when aggregate is null.</exception>
    public async Task<IWriteResult> SaveChanges<T>(OperationContext context, T aggregate, object? metadata = null, StreamMetadata? streamMetadata = null, CancellationToken token = default)
        where T : IAggregate<T>, IId
    {
        if (aggregate == null) throw new ArgumentNullException("aggregate cannot be null.");

        var streamId = Conventions.GetStreamIdConvention(context,typeof(T), aggregate.Id);
        var evData = MakeEvents(context,aggregate.PendingEvents, metadata, aggregate);
        var r = await Client.AppendToStreamAsync(streamId, StreamRevision.FromInt64(aggregate.Version), evData, cancellationToken: token);
        aggregate.AckCommitted();

        if (streamMetadata != null)
            await Client.SetStreamMetadataAsync(streamId, StreamState.Any, streamMetadata.Value,null,null,null,token);

        var policy = GetPolicy<T>();
        if (policy != null && aggregate is IStatefull i && policy.ShouldMakeSnapshot(aggregate, i.InitializedWith))
            await AppendSnapshot(context,i.State, aggregate.Id, aggregate.Version, StreamState.Any, token);

        return r;
    }

    /// <summary>
    /// Saves a new aggregate by appending its pending events to a new stream.
    /// </summary>
    /// <typeparam name="T">The type of aggregate.</typeparam>
    /// <param name="context">The operation context for the save operation.</param>
    /// <param name="aggregate">The new aggregate with pending events to save. Cannot be null.</param>
    /// <param name="metadata">Optional metadata to attach to all events.</param>
    /// <param name="streamMetadata">Optional stream metadata configuration.</param>
    /// <param name="token">Cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>The write result indicating success or failure.</returns>
    /// <exception cref="ArgumentNullException">Thrown when aggregate is null.</exception>
    public async Task<IWriteResult> SaveNew<T>(OperationContext context, T aggregate, object? metadata = null, StreamMetadata? streamMetadata = null, CancellationToken token = default)
        where T : IAggregate<T>, IId
    {
        if (aggregate == null) throw new ArgumentNullException("aggregate cannot be null.");

        var streamId = Conventions.GetStreamIdConvention(context,typeof(T), aggregate.Id);
        var evData = MakeEvents(context,aggregate.PendingEvents, metadata, aggregate);
        var r = await Client.AppendToStreamAsync(streamId, StreamState.NoStream, evData, cancellationToken: token);
        aggregate.AckCommitted();

        if (streamMetadata != null)
            await Client.SetStreamMetadataAsync(streamId, StreamState.Any, streamMetadata.Value, null, null, null, token);

        var policy = GetPolicy<T>();
        if (policy != null && aggregate is IStatefull i && policy.ShouldMakeSnapshot(aggregate, i.InitializedWith))
            await AppendSnapshot(context,i.State, aggregate.Id, aggregate.Version, StreamState.NoStream, token);

        return r;
    }
    

    /// <summary>
    /// Gets the latest state snapshot for a given ID.
    /// </summary>
    /// <typeparam name="T">The type of state to retrieve.</typeparam>
    /// <param name="context">The operation context for the get operation.</param>
    /// <param name="id">The ID identifying the state stream.</param>
    /// <param name="streamId">Optional stream ID. If null, determined by naming conventions.</param>
    /// <param name="token">Cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>The latest state with its metadata, or null if no state exists.</returns>
    public async Task<SubscriptionRunnerState<T>?> GetState<T>(OperationContext context, object id, string? streamId = null, CancellationToken token = default) where T:class
    {
        var streamType = typeof(T);
        streamId ??= Conventions.GetStreamIdStateConvention(context,streamType, id);
        var c = new SingleTypeConverter(streamType);
        var e = await ReadFull(context,streamId, c.Convert, StreamPosition.End, Direction.Backwards, 1, token).ToArrayAsync();
        if (!e.Any()) return null;

        //TODO: DuckTyping
        var (evt, m) = e[0];
        _versionTyping.SetVersion(evt, m.SourceStreamPosition);
        IdDuckTyping.Instance.SetId(evt, id);
        return new SubscriptionRunnerState<T>((T)evt, m);
    }

    /// <summary>
    /// Gets the latest snapshot for an aggregate by type and ID.
    /// </summary>
    /// <param name="context">The operation context for the get operation.</param>
    /// <param name="id">The aggregate ID.</param>
    /// <param name="snapshotType">The type of snapshot to retrieve.</param>
    /// <param name="token">Cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>The snapshot if it exists, or null if no snapshot is found.</returns>
    /// <exception cref="ArgumentNullException">Thrown when snapshotType is null.</exception>
    public async Task<Snapshot?> GetSnapshot(OperationContext context, object id, Type snapshotType, CancellationToken token = default)
    {
        if (snapshotType == null) throw new ArgumentNullException("snapshotType cannot be null.");

        var streamId = Conventions.GetStreamIdSnapshotConvention(context,snapshotType, id);
        var c = new SingleTypeConverter(snapshotType);
        var e = await ReadFull(context,streamId, c.Convert, StreamPosition.End, Direction.Backwards, 1, token).ToArrayAsync();
        if (!e.Any()) return null;

        var (evt, m) = e[0];
        var s = (Snapshot)Activator.CreateInstance(typeof(Snapshot<>).MakeGenericType(snapshotType));
        s.Created = m.Created().Value;
        s.Value = evt;
        s.Version = m.SnapshotVersion() ?? 0;
        return s;
    }

    /// <summary>
    /// Gets the latest typed snapshot for an aggregate by ID.
    /// </summary>
    /// <typeparam name="T">The type of snapshot to retrieve.</typeparam>
    /// <param name="context">The operation context for the get operation.</param>
    /// <param name="id">The aggregate ID.</param>
    /// <param name="token">Cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>The typed snapshot if it exists, or null if no snapshot is found.</returns>
    public async Task<Snapshot<T>?> GetSnapshot<T>(OperationContext context, Guid id, CancellationToken token = default)
    {
        var s = await GetSnapshot(context,id, typeof(T), token);
        return (Snapshot<T>)s;
    }

    /// <summary>
    /// Appends a link to a stream based on event metadata.
    /// </summary>
    /// <param name="streamId">The target stream to append the link to. Cannot be null or empty.</param>
    /// <param name="metadata">The metadata of the event to link to, containing source stream ID and position.</param>
    /// <param name="state">Optional expected stream state. Defaults to Any.</param>
    /// <param name="token">Cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>The write result indicating success or failure.</returns>
    /// <exception cref="ArgumentException">Thrown when streamId is null or empty.</exception>
    public async Task<IWriteResult> AppendLink(string streamId, Metadata metadata, StreamState? state = null, CancellationToken token = default)
    {
        if (string.IsNullOrEmpty(streamId)) throw new ArgumentException("steamId cannot be null or empty.");

        var data = Encoding.UTF8.GetBytes($"{metadata.SourceStreamPosition}@{metadata.SourceStreamId}");
        const string eventType = "$>";

        return await Client.AppendToStreamAsync(streamId, state ?? StreamState.Any,
            new[] { new EventData(Uuid.NewUuid(), eventType, data) }, cancellationToken: token);
    }

    /// <summary>
    /// Appends a link to a stream pointing to a specific event in another stream.
    /// </summary>
    /// <param name="streamId">The target stream to append the link to. Cannot be null or empty.</param>
    /// <param name="streamPosition">The position of the event in the source stream.</param>
    /// <param name="streamSourceId">The ID of the source stream containing the event. Cannot be null or empty.</param>
    /// <param name="state">Optional expected stream state. Defaults to Any.</param>
    /// <param name="token">Cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>The write result indicating success or failure.</returns>
    /// <exception cref="ArgumentException">Thrown when streamId or streamSourceId is null or empty.</exception>
    public async Task<IWriteResult> AppendLink(string streamId, ulong streamPosition, string streamSourceId,
        StreamState? state = null, CancellationToken token = default)
    {
        if (string.IsNullOrEmpty(streamId)) throw new ArgumentException("steamId cannot be null or empty.");
        if (string.IsNullOrEmpty(streamSourceId)) throw new ArgumentException("StreamSourceId is required.");

        var data = Encoding.UTF8.GetBytes($"{streamPosition}@{streamSourceId}");
        const string eventType = "$>";

        return await Client.AppendToStreamAsync(streamId, state ?? StreamState.Any,
            new[] { new EventData(Uuid.NewUuid(), eventType, data) }, cancellationToken:token);
    }


    private ISnapshotPolicy? GetPolicy<TOwner>()
    {
        return _policies.GetOrAdd(typeof(TOwner), x => Conventions.SnapshotPolicyFactoryConvention(x));
    }

    /// <summary>
    /// Gets or creates an extension object for the plumber engine.
    /// </summary>
    /// <typeparam name="T">The type of extension to retrieve or create.</typeparam>
    /// <returns>The extension instance.</returns>
    public T GetExtension<T>()
    {
        return (T)_extension.GetOrAdd(typeof(T), x => Activator.CreateInstance<T>());
    }

    public void SetExtension<T>(T extension)
    {
        _extension[typeof(T)] = extension!;
    }

    /// <summary>
    /// Gets a serializer for the specified type.
    /// </summary>
    /// <param name="t">The type to serialize.</param>
    /// <returns>An object serializer instance.</returns>
    private IObjectSerializer Serializer(Type t)
    {
        return _serializers.GetOrAdd(t, SerializerFactory);
    }

    /// <summary>
    /// Creates a new instance of PlumberEngine with optional configuration.
    /// </summary>
    /// <param name="settings">Optional EventStore connection settings. If null, defaults to localhost with admin credentials.</param>
    /// <param name="configure">Optional configuration action for customizing the plumber behavior.</param>
    /// <returns>A configured PlumberEngine instance.</returns>
    public static PlumberEngine Create(EventStoreClientSettings? settings = null, Action<IPlumberConfig>? configure = null)
    {
        settings ??=
            EventStoreClientSettings.Create("esdb://admin:changeit@localhost:2113?tls=false&tlsVerifyCert=false");
        var cfg = new PlumberConfig();
        configure?.Invoke(cfg);

        return new PlumberEngine(settings, cfg);
    }

    /// <summary>
    /// Deserializes an event record into a domain object with its metadata. This method is called internally from subscriptions.
    /// </summary>
    /// <param name="context">The operation context for the read operation.</param>
    /// <param name="er">The event record to deserialize.</param>
    /// <param name="eLink">Optional link event record if the event is a link.</param>
    /// <param name="t">The type to deserialize the event into.</param>
    /// <returns>A tuple containing the deserialized event object and its metadata.</returns>
    internal (object, Metadata) ReadEventData(OperationContext context, EventRecord er, EventRecord? eLink, Type t)
    {
        var streamIdSuffix = er.EventStreamId.Substring(er.EventStreamId.IndexOf('-') + 1);
        if (!Guid.TryParse(streamIdSuffix, out var aggregateId))
            aggregateId = streamIdSuffix.ToGuid();

        var s = Serializer(t);
        var ev = s.Deserialize(context,er.Data.Span, t)!;
        var m = s.ParseMetadata(context, er.Metadata.Span);

        var metadata = MetadataFactory.Create(aggregateId, er.EventStreamId, er.EventId.ToGuid(),
            er.EventNumber.ToInt64(), eLink?.EventNumber.ToInt64(), m);
        return (ev, metadata);
    }

    private IEnumerable<EventData> MakeEvents(OperationContext context, IEnumerable<object> events, object? metadata, IAggregate? agg = null)
    {
        var evData = events.Select(x =>
        {
            var m = Conventions.GetMetadata(context,agg, x, metadata);
            var evName = Conventions.GetEventNameConvention( agg?.GetType(), x.GetType());
            var evId = Conventions.GetEventIdConvention(context, agg, x);
            return MakeEvent(context,evId, evName, x, m);
        });
        return evData;
    }

    private EventData MakeEvent(OperationContext context, Uuid evId, string evName, object data, object m)
    {
        var s = Serializer(data.GetType());
        return new EventData(evId, evName, s.Serialize(context,data), s.Serialize(context,m), s.ContentType);
    }
}

/// <summary>
/// Represents stream metadata configuration for EventStore streams.
/// </summary>
public readonly record struct StreamMetadata
{
    /// <summary>
    /// Gets the maximum age for events in the stream.
    /// </summary>
    public TimeSpan? MaxAge { get; init; }

    /// <summary>
    /// Gets the position before which events will be truncated.
    /// </summary>
    public StreamPosition? TruncateBefore { get; init; }

    /// <summary>
    /// Gets the cache control duration for the stream metadata.
    /// </summary>
    public TimeSpan? CacheControl { get; init; }

    /// <summary>
    /// Gets the access control list for the stream.
    /// </summary>
    public StreamAcl? Acl { get; init; }

    /// <summary>
    /// Gets the maximum number of events allowed in the stream.
    /// </summary>
    public int? MaxCount { get; init; }

    /// <summary>
    /// Gets custom metadata as a JSON document.
    /// </summary>
    public JsonDocument? Custom { get; init; }

    /// <summary>
    /// Implicitly converts MicroPlumberd StreamMetadata to EventStore StreamMetadata.
    /// </summary>
    /// <param name="m">The MicroPlumberd stream metadata.</param>
    public static implicit operator EventStore.Client.StreamMetadata(StreamMetadata m)
    {
        return new EventStore.Client.StreamMetadata(m.MaxCount, m.MaxAge, m.TruncateBefore, m.CacheControl, m.Acl, m.Custom);
    }
}