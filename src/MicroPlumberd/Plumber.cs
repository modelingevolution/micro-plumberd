using System.Collections.Concurrent;
using System.IO;
using System.Text;
using EventStore.Client;

namespace MicroPlumberd;

/// <summary>
///     Root class for ED plumbing.
/// </summary>
public class Plumber : IPlumber, IPlumberReadOnlyConfig
{
    private readonly ConcurrentDictionary<Type, object> _extension = new();
    private readonly ConcurrentDictionary<Type, ISnapshotPolicy> _policies = new();
    private readonly TypeHandlerRegisters _typeHandlerRegisters;
    private readonly ConcurrentDictionary<Type, IObjectSerializer> _serializers = new();
    private ProjectionRegister? _projectionRegister;

    internal Plumber(EventStoreClientSettings settings, PlumberConfig? config = null)
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
    public Func<Type, IObjectSerializer> SerializerFactory { get; }
    public IReadOnlyConventions Conventions { get; }

    public ISubscriptionRunner Subscribe(string streamName, FromRelativeStreamPosition start,
        UserCredentials? userCredentials = null, CancellationToken cancellationToken = new())
    {
        if(start.Count == 0)
            return new SubscriptionRunner(this,
            Client.SubscribeToStream(streamName, start.StartPosition, true, userCredentials, cancellationToken));
        {
            

            return new DelayedSubscriptionRunner(this, streamName, start, userCredentials, cancellationToken);

        }
    }

    public Task<IAsyncDisposable> SubscribeEventHandler<TEventHandler>(TypeEventConverter mapFunc, IEnumerable<string>? eventTypes,
        TEventHandler? eh = default, string? outputStream = null, FromStream? start = null,
        bool ensureOutputStreamProjection = true) where TEventHandler : class, IEventHandler
    {
        return SubscribeEventHandler<TEventHandler>(mapFunc, eventTypes, eh, outputStream,
            start != null ? (FromRelativeStreamPosition)start.Value : null, ensureOutputStreamProjection);
    }

    
    public async Task<IAsyncDisposable> SubscribeEventHandler<TEventHandler>(TEventHandler? eh = default, string? outputStream = null,
        FromRelativeStreamPosition? start = null, bool ensureOutputStreamProjection = true) where TEventHandler : class, IEventHandler, ITypeRegister
    {
        return await SubscribeEventHandler(_typeHandlerRegisters.GetEventNameConverterFor<TEventHandler>()!,
            _typeHandlerRegisters.GetEventNamesFor<TEventHandler>(),
            eh, outputStream, start, ensureOutputStreamProjection);
    }

    public async Task<IAsyncDisposable> SubscribeEventHandler<TEventHandler>(TypeEventConverter mapFunc,
        IEnumerable<string>? eventTypes, TEventHandler? eh = default, string? outputStream = null,
        FromRelativeStreamPosition? start = null, bool ensureOutputStreamProjection = true)
        where TEventHandler : class, IEventHandler
    {
        eventTypes ??= Array.Empty<string>();

        outputStream ??= Conventions.OutputStreamModelConvention(typeof(TEventHandler));
        if (ensureOutputStreamProjection)
            await ProjectionManagementClient.EnsureJoinProjection(outputStream, ProjectionRegister, eventTypes);
        var sub = Subscribe(outputStream, start ?? FromStream.Start);
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
        bool ensureOutputStreamProjection = true)
        where TEventHandler : class, IEventHandler
    {
        var handlerType = typeof(TEventHandler);
        startFrom ??= StreamPosition.End;
        outputStream ??= Conventions.OutputStreamModelConvention(handlerType);
        groupName ??= Conventions.GroupNameModelConvention(handlerType);
        if (ensureOutputStreamProjection)
            await ProjectionManagementClient.EnsureJoinProjection(outputStream, ProjectionRegister, events);

        try
        {
            await PersistentSubscriptionClient.GetInfoToStreamAsync(outputStream, groupName);
        }
        catch (PersistentSubscriptionNotFoundException)
        {
            await PersistentSubscriptionClient.CreateToStreamAsync(outputStream, groupName,
                new PersistentSubscriptionSettings(true, startFrom));
        }

        var sub = SubscribePersistently(outputStream, groupName);
        if (model == null)
            await sub.WithHandler<TEventHandler>(mapFunc);
        else
            await sub.WithHandler(model, mapFunc);
        return sub;
        return sub;
    }

    public Task<IAsyncDisposable> SubscribeEventHandlerPersistently<TEventHandler>(TEventHandler? model,
        string? outputStream = null, string? groupName = null, IPosition? startFrom = null,
        bool ensureOutputStreamProjection = true)
        where TEventHandler : class, IEventHandler, ITypeRegister
    {
        return SubscribeEventHandlerPersistently(_typeHandlerRegisters.GetEventNameConverterFor<TEventHandler>(),
            _typeHandlerRegisters.GetEventNamesFor<TEventHandler>(),
            model, outputStream, groupName, startFrom, ensureOutputStreamProjection);
    }


    public ISubscriptionRunner SubscribePersistently(string streamName, string groupName, int bufferSize = 10,
        UserCredentials? userCredentials = null, CancellationToken cancellationToken = new())
    {
        return new PersistentSubscriptionRunner(this,
            PersistentSubscriptionClient.SubscribeToStream(streamName, groupName, bufferSize, userCredentials,
                cancellationToken));
    }

    public async Task Rehydrate<T>(T model, Guid id, StreamPosition? position = null)
        where T : IEventHandler, ITypeRegister
    {
        var streamId = Conventions.GetStreamIdConvention(typeof(T), id);
        await Rehydrate(model, streamId, position);
    }

    public async Task Rehydrate<T>(T model, string streamId, StreamPosition? position = null)
        where T : IEventHandler, ITypeRegister
    {
        var pos = position ?? StreamPosition.Start;
        var items = Client.ReadStreamAsync(Direction.Forwards, streamId, pos, resolveLinkTos: true);
        var registry = _typeHandlerRegisters.GetEventNameConverterFor<T>();
        var vAware = model as IVersionAware;

        if (await items.ReadState == ReadState.StreamNotFound) return;

        await foreach (var i in items)
        {
            if (!registry(i.Event.EventType, out var t))
                continue;
            var (evt, metadata) = ReadEventData(i.Event, t);
            await model.Handle(metadata, evt);
            vAware?.Increase();
        }
    }

    public async Task<IEventRecord?> FindEventInStream(string streamId, Guid id,
        TypeEventConverter eventMapping, Direction scanDirection = Direction.Backwards)
    {
        return await FindEventInStream<object>(streamId, id, eventMapping, scanDirection);
    }

    public async Task<IEventRecord<TEvent>?> FindEventInStream<TEvent>(string streamId, Guid id,
        TypeEventConverter? eventMapping = null, Direction scanDirection = Direction.Backwards)
    {
        var items = Client.ReadStreamAsync(Direction.Forwards, streamId, StreamPosition.Start);

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

            var (evt, metadata) = ReadEventData(i.Event, t);
            return new EventRecord<TEvent> { Event = (TEvent)evt, Metadata = metadata };
        }

        return null;
    }

    public ISubscriptionSet SubscribeSet()
    {
        return new SubscriptionSet(this);
    }

    public IAsyncEnumerable<object> Read<TOwner>(object id, StreamPosition? start = null, Direction? direction = null,
        long maxCount = long.MaxValue) where TOwner : ITypeRegister
    {
        start ??= StreamPosition.Start;

        var streamId = Conventions.GetStreamIdConvention(typeof(TOwner), id);
        var registry = _typeHandlerRegisters.GetEventNameConverterFor<TOwner>();
        return Read(streamId, registry, start, direction, maxCount);
    }

    public IAsyncEnumerable<object> Read<TOwner>(StreamPosition? start = null, Direction? direction = null,
        long maxCount = long.MaxValue) where TOwner : ITypeRegister
    {
        var streamId = Conventions.ProjectionCategoryStreamConvention(typeof(TOwner));
        var evNameConv = _typeHandlerRegisters.GetEventNameConverterFor<TOwner>();
        return Read(streamId, evNameConv, start, direction, maxCount);
    }

    public async IAsyncEnumerable<(object, Metadata)> ReadFull(string streamId, TypeEventConverter converter,
        StreamPosition? start = null, Direction? direction = null, long maxCount = long.MaxValue)
    {
        var d = direction ?? Direction.Forwards;
        var p = start ?? StreamPosition.Start;

        var items = Client.ReadStreamAsync(d, streamId, p, resolveLinkTos: true, maxCount: maxCount);
        if (await items.ReadState == ReadState.StreamNotFound) yield break;

        var events = items.Select(x => new
                { ResolvedEvent = x, EventType = converter(x.Event.EventType, out var t) ? t : null })
            .Where(x => x.EventType != null)
            .Select(ev => ReadEventData(ev.ResolvedEvent.Event, ev.EventType));
        await foreach (var i in events)
            yield return i;
    }

    public async IAsyncEnumerable<object> Read(string streamId, TypeEventConverter converter,
        StreamPosition? start = null, Direction? direction = null, long maxCount = long.MaxValue)
    {
        var d = direction ?? Direction.Forwards;
        var p = start ?? StreamPosition.Start;

        var items = Client.ReadStreamAsync(d, streamId, p, resolveLinkTos: true, maxCount: maxCount);
        if (await items.ReadState == ReadState.StreamNotFound) yield break;

        var events = items.Select(x => new
                { ResolvedEvent = x, EventType = converter(x.Event.EventType, out var t) ? t : null })
            .Where(x => x.EventType != null)
            .Select(ev => Serializer(ev.EventType).Deserialize(ev.ResolvedEvent.Event.Data.Span, ev.EventType!));
        await foreach (var i in events)
            yield return i;
    }

    public async Task<TOwner> Get<TOwner>(object id)
        where TOwner : IAggregate<TOwner>, ITypeRegister, IId
    {
        var sp = StreamPosition.Start;
        var aggregate = TOwner.Empty(id);
        if (GetPolicy<TOwner>() != null && aggregate is IStatefull i)
        {
            var snapshot = await GetSnapshot(id, i.SnapshotType);
            if (snapshot != null)
            {
                i.Initialize(snapshot.Value, new StateInfo(snapshot.Version, snapshot.Created));
                sp = StreamPosition.FromInt64(snapshot.Version + 1);
            }
        }

        await aggregate.Rehydrate(Read<TOwner>(id, sp));
        return aggregate;
    }

    

    public async Task<IWriteResult> AppendEvents(string streamId, StreamRevision rev, IEnumerable<object> events,
        object? metadata = null)
    {
        var evData = MakeEvents(events, metadata);

        return await Client.AppendToStreamAsync(streamId, rev, evData);
    }

    public async Task<IWriteResult> AppendEvents(string streamId, StreamState state, IEnumerable<object> events,
        object? metadata = null)
    {
        var evData = MakeEvents(events, metadata);

        var r = await Client.AppendToStreamAsync(streamId, state, evData);
        return r;
    }

    public async Task<IWriteResult> AppendSnapshot(object snapshot, object id, long version, StreamState state)
    {
        var m = Conventions.GetMetadata(null, snapshot, new { SnapshotVersion = version });
        var stateType = snapshot.GetType();
        var streamId = Conventions.GetStreamIdSnapshotConvention(stateType, id);
        var evId = Conventions.GetEventIdConvention(null, snapshot);
        var evData = MakeEvent(evId, Conventions.SnapshotEventNameConvention(stateType), snapshot, m);
        var r = await Client.AppendToStreamAsync(streamId, state, [evData]);
        return r;
    }

    public async Task<IWriteResult> AppendEvent(object evt, object? id=null, object? metadata = null, StreamState? state=null, string? evtName=null)
    {
        if (evt == null) throw new ArgumentException("evt cannot be null.");
        

        evtName ??= Conventions.GetEventNameConvention(null, evt.GetType());
        var m = Conventions.GetMetadata(null, evt, metadata);
        var st = state ?? StreamState.Any;
        var streamId = Conventions.StreamNameFromEventConvention(evt.GetType(), id);
        var evId = Conventions.GetEventIdConvention(null, evt);
        var evData = MakeEvent(evId, evtName, evt, m);

        var r = await Client.AppendToStreamAsync(streamId, st, [evData]);
        return r;
    }
    public async Task<IWriteResult> AppendEvent(string streamId, StreamState state, string evtName, object evt, object? metadata = null)
    {
        if (string.IsNullOrEmpty(streamId)) throw new ArgumentException("steamId cannot be null or empty.");
        if (string.IsNullOrEmpty(evtName)) throw new ArgumentException("evtName cannot be null or empty.");

        var m = Conventions.GetMetadata(null, evt, metadata);
        var evId = Conventions.GetEventIdConvention(null, evt);
        var evData = MakeEvent(evId, evtName, evt, m);

        var r = await Client.AppendToStreamAsync(streamId, state, [evData]);
        return r;
    }

    public async Task<IWriteResult> SaveChanges<T>(T aggregate, object? metadata = null)
        where T : IAggregate<T>, IId
    {
        if (aggregate == null) throw new ArgumentNullException("aggregate cannot be null.");

        var streamId = Conventions.GetStreamIdConvention(typeof(T), aggregate.Id);
        var evData = MakeEvents(aggregate.PendingEvents, metadata, aggregate);
        var r = await Client.AppendToStreamAsync(streamId, StreamRevision.FromInt64(aggregate.Version), evData);
        aggregate.AckCommitted();

        var policy = GetPolicy<T>();
        if (policy != null && aggregate is IStatefull i && policy.ShouldMakeSnapshot(aggregate, i.InitializedWith))
            await AppendSnapshot(i.State, aggregate.Id, aggregate.Version, StreamState.Any);

        return r;
    }


    public async Task<IWriteResult> SaveNew<T>(T aggregate, object? metadata = null)
        where T : IAggregate<T>, IId
    {
        if (aggregate == null) throw new ArgumentNullException("aggregate cannot be null.");

        var streamId = Conventions.GetStreamIdConvention(typeof(T), aggregate.Id);
        var evData = MakeEvents(aggregate.PendingEvents, metadata, aggregate);
        var r = await Client.AppendToStreamAsync(streamId, StreamState.NoStream, evData);
        aggregate.AckCommitted();

        var policy = GetPolicy<T>();
        if (policy != null && aggregate is IStatefull i && policy.ShouldMakeSnapshot(aggregate, i.InitializedWith))
            await AppendSnapshot(i.State, aggregate.Id, aggregate.Version, StreamState.NoStream);

        return r;
    }

    public async Task<Snapshot?> GetSnapshot(object id, Type snapshotType)
    {
        if (snapshotType == null) throw new ArgumentNullException("snapshotType cannot be null.");

        var streamId = Conventions.GetStreamIdSnapshotConvention(snapshotType, id);
        var c = new SnapshotConverter(snapshotType);
        var e = await ReadFull(streamId, c.Convert, StreamPosition.End, Direction.Backwards, 1).ToArrayAsync();
        if (!e.Any()) return null;

        var (evt, m) = e[0];
        var s = (Snapshot)Activator.CreateInstance(typeof(Snapshot<>).MakeGenericType(snapshotType));
        s.Created = m.Created().Value;
        s.Value = evt;
        s.Version = m.SnapshotVersion() ?? 0;
        return s;
    }

    public async Task<Snapshot<T>?> GetSnapshot<T>(Guid id)
    {
        var s = await GetSnapshot(id, typeof(T));
        return (Snapshot<T>)s;
    }

    /// <summary>
    ///     Appends a link to the stream based on metadata loaded from somewhere else.
    /// </summary>
    /// <param name="streamId">Full name of the stream.</param>
    /// <param name="metadata">Event's metadata that link will point to.</param>
    /// <param name="state">StreamState, default is Any</param>
    /// <returns></returns>
    public async Task<IWriteResult> AppendLink(string streamId, Metadata metadata, StreamState? state = null)
    {
        if (string.IsNullOrEmpty(streamId)) throw new ArgumentException("steamId cannot be null or empty.");

        var data = Encoding.UTF8.GetBytes($"{metadata.SourceStreamPosition}@{metadata.SourceStreamId}");
        const string eventType = "$>";

        return await Client.AppendToStreamAsync(streamId, state ?? StreamState.Any,
            new[] { new EventData(Uuid.NewUuid(), eventType, data) });
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
    public static IPlumber Create(EventStoreClientSettings? settings = null, Action<IPlumberConfig>? configure = null)
    {
        settings ??=
            EventStoreClientSettings.Create("esdb://admin:changeit@localhost:2113?tls=false&tlsVerifyCert=false");
        var cfg = new PlumberConfig();
        configure?.Invoke(cfg);

        return new Plumber(settings, cfg);
    }

    /// <summary>
    ///     This method is called only from subscriptions.
    /// </summary>
    /// <param name="er"></param>
    /// <param name="t"></param>
    /// <returns></returns>
    internal (object, Metadata) ReadEventData(EventRecord er, Type t)
    {
        var aggregateId = Guid.Parse(er.EventStreamId.Substring(er.EventStreamId.IndexOf('-') + 1));
        var s = Serializer(t);
        var ev = s.Deserialize(er.Data.Span, t)!;
        var m = s.ParseMetadata(er.Metadata.Span);

        var metadata = new Metadata(aggregateId, er.EventId.ToGuid(), er.EventNumber.ToInt64(), er.EventStreamId, m);
        return (ev, metadata);
    }

    private IEnumerable<EventData> MakeEvents(IEnumerable<object> events, object? metadata, IAggregate? agg = null)
    {
        var evData = events.Select(x =>
        {
            var m = Conventions.GetMetadata(agg, x, metadata);
            var evName = Conventions.GetEventNameConvention(agg?.GetType(), x.GetType());
            var evId = Conventions.GetEventIdConvention(agg, x);
            return MakeEvent(evId, evName, x, m);
        });
        return evData;
    }

    private EventData MakeEvent(Uuid evId, string evName, object data, object m)
    {
        var s = Serializer(data.GetType());
        return new EventData(evId, evName, s.Serialize(data), s.Serialize(m), s.ContentType);
    }
}