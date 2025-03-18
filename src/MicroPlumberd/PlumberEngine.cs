using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using EventStore.Client;
using MicroPlumberd.Utils;

namespace MicroPlumberd;

/// <summary>
///     Root class for ED plumbing.
/// </summary>
public class PlumberEngine : IPlumberReadOnlyConfig
{
    private readonly ConcurrentDictionary<Type, object> _extension = new();
    private readonly ConcurrentDictionary<Type, ISnapshotPolicy> _policies = new();
    private readonly TypeHandlerRegisters _typeHandlerRegisters;
    private readonly ConcurrentDictionary<Type, IObjectSerializer> _serializers = new();
    
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
        SerializerFactory = config.SerializerFactory;
        ServiceProvider = config.ServiceProvider;
        _extension = config.Extension; // Shouldn't we make a copy?
        _typeHandlerRegisters = new TypeHandlerRegisters(Conventions.GetEventNameConvention);
        this._errorHandle = config.ErrorHandlePolicy;
        config.OnCreated(this);
    }
    public IPlumberReadOnlyConfig Config => this;

    public ITypeHandlerRegisters TypeHandlerRegisters => _typeHandlerRegisters;
    public EventStoreClient Client { get; }

    public IProjectionRegister ProjectionRegister =>
        _projectionRegister ??= new ProjectionRegister(ProjectionManagementClient);

    public EventStorePersistentSubscriptionsClient PersistentSubscriptionClient { get; }
    public EventStoreProjectionManagementClient ProjectionManagementClient { get; }

    /// <summary>
    /// </summary>
    public IServiceProvider ServiceProvider { get; }

    public Task<ErrorHandleDecision> HandleError(Exception ex,OperationContext context, CancellationToken t) => _errorHandle(ex, context, t);
    public Func<Type, IObjectSerializer> SerializerFactory { get; }
    public IReadOnlyConventions Conventions { get; }

    public ISubscriptionRunner Subscribe(string streamName, FromRelativeStreamPosition start,
        UserCredentials? userCredentials = null, CancellationToken cancellationToken = default)
    {
        if (start.Count == 0)
            return new SubscriptionRunner(this,
                new SubscriptionRunnerState(start.StartPosition, Client, streamName, userCredentials, cancellationToken));
        return new SubscriptionSeeker(this, streamName, start, userCredentials, cancellationToken);
    }

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
    public async Task<IAsyncDisposable> SubscribeEventHandler<TEventHandler>(TEventHandler? eh = null,
        string? outputStream = null,
        FromRelativeStreamPosition? start = null, bool ensureOutputStreamProjection = true,
        CancellationToken token = default) where TEventHandler : class, IEventHandler, ITypeRegister
    {
        return await SubscribeEventHandler(_typeHandlerRegisters.GetEventNameConverterFor<TEventHandler>()!,
            _typeHandlerRegisters.GetEventNamesFor<TEventHandler>(),
            eh, outputStream, start, ensureOutputStreamProjection, token);
    }
    public async Task<IAsyncDisposable> SubscribeStateEventHandler<TEventHandler>(TEventHandler? eh = null,
        string? outputStream = null,
        FromRelativeStreamPosition? start = null, bool ensureOutputStreamProjection = true,
        CancellationToken token = default) where TEventHandler : class, IEventHandler, ITypeRegister
    {
        var evts = TEventHandler.Types.Select(x => this.Conventions.SnapshotEventNameConvention(x)).ToArray();
        return await SubscribeStateEventHandler(evts, eh, outputStream, start, ensureOutputStreamProjection, token);
    }
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

    public Task<IAsyncDisposable> SubscribeEventHandlerPersistently<TEventHandler>(TEventHandler? model=null,
        string? outputStream = null, string? groupName = null, IPosition? startFrom = null,
        bool ensureOutputStreamProjection = true, int minCheckPointCount = 1, CancellationToken token = default)
        where TEventHandler : class, IEventHandler, ITypeRegister
    {
        return SubscribeEventHandlerPersistently(_typeHandlerRegisters.GetEventNameConverterFor<TEventHandler>(),
            _typeHandlerRegisters.GetEventNamesFor<TEventHandler>(),
            model, outputStream, groupName, startFrom, ensureOutputStreamProjection, minCheckPointCount, token);
    }


    public ISubscriptionRunner SubscribePersistently(string streamName, string groupName, int bufferSize = 10, 
        UserCredentials? userCredentials = null, CancellationToken cancellationToken = default)
    {
        return new PersistentSubscriptionRunner(this,
            PersistentSubscriptionClient.SubscribeToStream(streamName, groupName, bufferSize,  userCredentials,
                cancellationToken));
    }

    public async Task Rehydrate<T>(OperationContext context, T model,  Guid id, StreamPosition? position = null, CancellationToken token = default)
        where T : IEventHandler, ITypeRegister
    {
        var streamId = Conventions.GetStreamIdConvention(context,typeof(T), id);
        await Rehydrate(context,model, streamId, position, token);
    }

    public async Task Rehydrate<T>(OperationContext context, T model, string streamId, StreamPosition? position = null, CancellationToken token = default)
        where T : IEventHandler, ITypeRegister
    {
        TypeEventConverter registry = _typeHandlerRegisters.GetEventNameConverterFor<T>();
        await Rehydrate(context,model, streamId, registry, position, token);
    }
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

    public async Task<IEventRecord?> FindEventInStream(OperationContext context, string streamId, Guid id,
        TypeEventConverter eventMapping, Direction scanDirection = Direction.Backwards, CancellationToken token = default)
    {
        return await FindEventInStream<object>(context,streamId, id, eventMapping, scanDirection, token);
    }

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

    public IEngineSubscriptionSet SubscribeSet()
    {
        return new SubscriptionSet(this);
    }

    public IAsyncEnumerable<object> Read<TOwner>(OperationContext context, object id, StreamPosition? start = null, Direction? direction = null,
        long maxCount = long.MaxValue, CancellationToken token = default) where TOwner : ITypeRegister
    {
        start ??= StreamPosition.Start;

        var streamId = Conventions.GetStreamIdConvention(context,typeof(TOwner), id);
        var registry = _typeHandlerRegisters.GetEventNameConverterFor<TOwner>();
        return Read(context,streamId, registry, start, direction, maxCount, token);
    }

    public IAsyncEnumerable<object> Read<TOwner>(OperationContext context, StreamPosition? start = null, Direction? direction = null,
        long maxCount = long.MaxValue, CancellationToken token = default) where TOwner : ITypeRegister
    {
        var streamId = Conventions.ProjectionCategoryStreamConvention(typeof(TOwner));
        var evNameConv = _typeHandlerRegisters.GetEventNameConverterFor<TOwner>();
        return Read(context, streamId, evNameConv, start, direction, maxCount, token);
    }

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

    

    public async Task<IWriteResult> AppendEvents(OperationContext context, string streamId, StreamRevision rev, IEnumerable<object> events,
        object? metadata = null, CancellationToken token = default)
    {
        var evData = MakeEvents(context,events, metadata);

        return await Client.AppendToStreamAsync(streamId, rev, evData, cancellationToken: token);
    }

    public async Task<IWriteResult> AppendEvents(OperationContext context, string streamId, StreamState state, IEnumerable<object> events,
        object? metadata = null, CancellationToken token = default)
    {
        var evData = MakeEvents(context,events, metadata);

        var r = await Client.AppendToStreamAsync(streamId, state, evData, cancellationToken: token);
        return r;
    }

    public Task<IWriteResult> AppendState<T>(OperationContext context, T state, CancellationToken token = default) => 
        AppendState(context,state, IdDuckTyping.Instance.GetId(state), _versionTyping.GetVersion(state), token);

    
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
        var metadata = new StreamMetadata(
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
    /// Appends an event to a stream in the Event Store.
    /// </summary>
    /// <param name="evt">The event to append. Cannot be <c>null</c>.</param>
    /// <param name="id">The optional identifier for the stream. If not provided, conventions will be used to determine the stream name.</param>
    /// <param name="metadata">Optional metadata associated with the event.</param>
    /// <param name="state">
    /// The expected state of the stream. Defaults to <see cref="StreamState.Any"/> if not specified.
    /// </param>
    /// <param name="evtName">
    /// The name of the event. If not provided, conventions will be used to determine the event name.
    /// </param>
    /// <param name="token">A <see cref="CancellationToken"/> to observe while waiting for the task to complete.</param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains an <see cref="IWriteResult"/> 
    /// indicating the result of the append operation.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="evt"/> is <c>null</c>.</exception>
    public async Task<IWriteResult> AppendEvent(OperationContext context, object evt, object? id=null, object? metadata = null, StreamState? state=null, string? evtName=null, CancellationToken token = default)
    {
        if (evt == null) throw new ArgumentException("evt cannot be null.");
        

        evtName ??= Conventions.GetEventNameConvention(null, evt.GetType());
        var m = Conventions.GetMetadata(context,null, evt, metadata);
        var st = state ?? StreamState.Any;
        var streamId = Conventions.StreamNameFromEventConvention(context,evt.GetType(), id);
        var evId = Conventions.GetEventIdConvention(context,null, evt);
        var evData = MakeEvent(context,evId, evtName, evt, m);

        var r = await Client.AppendToStreamAsync(streamId, st, [evData], cancellationToken:token);
        return r;
    }
    public async Task<IWriteResult> AppendEventToStream(OperationContext context, string streamId, object evt, StreamState? state = null, string? evtName = null,
        object? metadata = null, CancellationToken token = default)
    {
        if (string.IsNullOrEmpty(streamId)) throw new ArgumentException("steamId cannot be null or empty.");
        if (evt == null) throw new ArgumentException("Event cannot be null");

        StreamState st = state ?? StreamState.Any;
        var eventName = evtName ?? Conventions.GetEventNameConvention( null, evt.GetType());
        var m = Conventions.GetMetadata(context, null, evt, metadata);
        var evId = Conventions.GetEventIdConvention(context, null, evt);
        var evData = MakeEvent(context,evId, eventName, evt, m);

        var r = await Client.AppendToStreamAsync(streamId, st, [evData], cancellationToken: token);
        return r;
    }

    
    
    public async Task<IWriteResult> SaveChanges<T>(OperationContext context, T aggregate, object? metadata = null, CancellationToken token = default)
        where T : IAggregate<T>, IId
    {
        if (aggregate == null) throw new ArgumentNullException("aggregate cannot be null.");

        var streamId = Conventions.GetStreamIdConvention(context,typeof(T), aggregate.Id);
        var evData = MakeEvents(context,aggregate.PendingEvents, metadata, aggregate);
        var r = await Client.AppendToStreamAsync(streamId, StreamRevision.FromInt64(aggregate.Version), evData, cancellationToken: token);
        aggregate.AckCommitted();

        var policy = GetPolicy<T>();
        if (policy != null && aggregate is IStatefull i && policy.ShouldMakeSnapshot(aggregate, i.InitializedWith))
            await AppendSnapshot(context,i.State, aggregate.Id, aggregate.Version, StreamState.Any, token);

        return r;
    }


    public async Task<IWriteResult> SaveNew<T>(OperationContext context, T aggregate, object? metadata = null, CancellationToken token = default)
        where T : IAggregate<T>, IId
    {
        if (aggregate == null) throw new ArgumentNullException("aggregate cannot be null.");

        var streamId = Conventions.GetStreamIdConvention(context,typeof(T), aggregate.Id);
        var evData = MakeEvents(context,aggregate.PendingEvents, metadata, aggregate);
        var r = await Client.AppendToStreamAsync(streamId, StreamState.NoStream, evData, cancellationToken: token);
        aggregate.AckCommitted();

        var policy = GetPolicy<T>();
        if (policy != null && aggregate is IStatefull i && policy.ShouldMakeSnapshot(aggregate, i.InitializedWith))
            await AppendSnapshot(context,i.State, aggregate.Id, aggregate.Version, StreamState.NoStream, token);

        return r;
    }
    

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

    public async Task<Snapshot<T>?> GetSnapshot<T>(OperationContext context, Guid id, CancellationToken token = default)
    {
        var s = await GetSnapshot(context,id, typeof(T), token);
        return (Snapshot<T>)s;
    }

    /// <summary>
    ///     Appends a link to the stream based on metadata loaded from somewhere else.
    /// </summary>
    /// <param name="streamId">Full name of the stream.</param>
    /// <param name="metadata">Event's metadata that link will point to.</param>
    /// <param name="state">StreamState, default is Any</param>
    /// <param name="token"></param>
    /// <returns></returns>
    public async Task<IWriteResult> AppendLink(string streamId, Metadata metadata, StreamState? state = null, CancellationToken token = default)
    {
        if (string.IsNullOrEmpty(streamId)) throw new ArgumentException("steamId cannot be null or empty.");

        var data = Encoding.UTF8.GetBytes($"{metadata.SourceStreamPosition}@{metadata.SourceStreamId}");
        const string eventType = "$>";

        return await Client.AppendToStreamAsync(streamId, state ?? StreamState.Any,
            new[] { new EventData(Uuid.NewUuid(), eventType, data) }, cancellationToken: token);
    }
    /// <summary>
    /// Appends the link.
    /// </summary>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="streamPosition">Stream position in original stream</param>
    /// <param name="streamSourceId">The stream source identifier.</param>
    /// <param name="state">Optional expected stream state.</param>
    /// <returns></returns>
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

    public T GetExtension<T>() where T : new()
    {
        return (T)_extension.GetOrAdd(typeof(T), x => new T());
    }

    private IObjectSerializer Serializer(Type t)
    {
        return _serializers.GetOrAdd(t, SerializerFactory);
    }

    /// <summary>
    ///     Creates instance of IPlumber.
    /// </summary>
    /// <param name="settings">Connection settings to EventStore</param>
    /// <param name="configure">Additional configuration</param>
    /// <returns></returns>
    public static PlumberEngine Create(EventStoreClientSettings? settings = null, Action<IPlumberConfig>? configure = null)
    {
        settings ??=
            EventStoreClientSettings.Create("esdb://admin:changeit@localhost:2113?tls=false&tlsVerifyCert=false");
        var cfg = new PlumberConfig();
        configure?.Invoke(cfg);

        return new PlumberEngine(settings, cfg);
    }

    /// <summary>
    ///     This method is called only from subscriptions.
    /// </summary>
    /// <param name="er"></param>
    /// <param name="eLink"></param>
    /// <param name="t"></param>
    /// <returns></returns>
    internal (object, Metadata) ReadEventData(OperationContext context, EventRecord er, EventRecord? eLink, Type t)
    {
        var streamIdSuffix = er.EventStreamId.Substring(er.EventStreamId.IndexOf('-') + 1);
        if (!Guid.TryParse(streamIdSuffix, out var aggregateId)) 
            aggregateId = streamIdSuffix.ToGuid();

        var s = Serializer(t);
        var ev = s.Deserialize(context,er.Data.Span, t)!;
        var m = s.ParseMetadata(context, er.Metadata.Span);

        long? linkStreamPosition = eLink?.EventNumber.ToInt64();
        long sourceStreamPosition = er.EventNumber.ToInt64();
        
        var metadata = new Metadata(aggregateId, er.EventId.ToGuid(), sourceStreamPosition, linkStreamPosition, er.EventStreamId, m);
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