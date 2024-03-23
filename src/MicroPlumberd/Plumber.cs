
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using EventStore.Client;
using Google.Protobuf.Reflection;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;

namespace MicroPlumberd;

public interface IPlumberConfig : IExtension
{
    IObjectSerializer Serializer { get; set; }
    IConventions Conventions { get; }
    IServiceProvider ServiceProvider { get; set; }
}

class PlumberConfig : IPlumberConfig
{
    internal readonly ConcurrentDictionary<Type, object> Extension = new();
    public T GetExtension<T>() where T : new() => (T)Extension.GetOrAdd(typeof(T), x => new T());

    private IServiceProvider _serviceProvider = new ActivatorServiceProvider();
    private IObjectSerializer _serializer = new ObjectSerializer();

    public IObjectSerializer Serializer
    {
        get => _serializer;
        set
        {
            if(value == null!) throw new ArgumentNullException("ObjectSerializer cannot be null.");
            _serializer = value;
        }
    }

    public IConventions Conventions { get; } = new Conventions();

    public IServiceProvider ServiceProvider
    {
        get => _serviceProvider;
        set
        {
            if (value == null!) throw new ArgumentNullException("ServiceProvider cannot be null.");
            _serviceProvider = value;
        }
    }
}

class ActivatorServiceProvider : IServiceProvider
{
    public object? GetService(Type serviceType)
    {
        return Activator.CreateInstance(serviceType);
    }
}

public interface IEventRecord
{
    Metadata Metadata { get; }
    object Event { get; }
}

public interface IEventRecord<out TEvent> : IEventRecord
{
    new TEvent Event { get; } 
}
record EventRecord<TEvent> : IEventRecord<TEvent>
{
    public Metadata Metadata { get; init; }
    public TEvent Event { get; init; }
    object IEventRecord.Event => Event;
}

public interface ITypeHandlerRegister
{
    TypeEventConverter GetConverterFor<T>() where T:ITypeRegister;
    IEnumerable<KeyValuePair<string, Type>> GetItems<T>() where T : ITypeRegister;
    IEnumerable<string> GetEventsFor<T>() where T : ITypeRegister;
}

sealed class TypeHandlerRegister(EventNameConvention conventions) : ITypeHandlerRegister
{
    private readonly ConcurrentDictionary<Type, FrozenDictionary<string, Type>> _index = new();
    
    public TypeEventConverter GetConverterFor<T>() where T:ITypeRegister => Get<T>().TryGetValue!;

    private FrozenDictionary<string, Type> Get<T>() where T:ITypeRegister
    {
        var ownerType = typeof(T);
        return _index.GetOrAdd(ownerType, x => T.Types.ToFrozenDictionary(x => conventions(ownerType, x)));
    }
    
    public IEnumerable<KeyValuePair<string, Type>> GetItems<T>() where T : ITypeRegister
    {
        return Get<T>();
    }
    public IEnumerable<string> GetEventsFor<T>() where T : ITypeRegister => Get<T>().Keys;
}
public class Plumber : IPlumber, IPlumberConfig
{
    private readonly EventStoreClient _client;
    private readonly EventStorePersistentSubscriptionsClient _persistentSubscriptionClient;
    private readonly EventStoreProjectionManagementClient _projectionManagementClient;
    private readonly TypeHandlerRegister _typeHandlerRegister;
    public ITypeHandlerRegister TypeHandlerRegister => _typeHandlerRegister;
    public EventStoreClient Client => _client;
    public static IPlumber Create(EventStoreClientSettings? settings = null, Action<IPlumberConfig>? configure = null)
    {
        settings ??= EventStoreClientSettings.Create($"esdb://admin:changeit@localhost:2113?tls=false&tlsVerifyCert=false");
        var cfg = new PlumberConfig();
        configure?.Invoke(cfg);

        return new Plumber(settings, cfg);
    }
    internal Plumber(EventStoreClientSettings settings, PlumberConfig? config = null)
    {
        config ??= new PlumberConfig();
        _settings = settings;
        _client = new(settings);
        _persistentSubscriptionClient = new(settings);
        _projectionManagementClient = new (settings);
        this.Conventions = config.Conventions;
        this.Serializer = config.Serializer;
        this.ServiceProvider = config.ServiceProvider;
        this._extension = config.Extension; // Shouldn't we make a copy?
        this._typeHandlerRegister = new TypeHandlerRegister(this.Conventions.GetEventNameConvention);
    }

    private readonly ConcurrentDictionary<Type, object> _extension = new();
    public T GetExtension<T>() where T : new() => (T)_extension.GetOrAdd(typeof(T), x => new T());

    public IServiceProvider ServiceProvider { get; set; }
    public IObjectSerializer Serializer { get; set; }
    public IConventions Conventions { get; }
    private ProjectionRegister? _projectionRegister;
    private readonly EventStoreClientSettings _settings;
    public IProjectionRegister ProjectionRegister => _projectionRegister ??= new ProjectionRegister(_projectionManagementClient);
    
    public EventStorePersistentSubscriptionsClient PersistentSubscriptionClient => _persistentSubscriptionClient;
    public EventStoreProjectionManagementClient ProjectionManagementClient => _projectionManagementClient;

    public ISubscriptionRunner Subscribe(string streamName, FromStream start, 
        UserCredentials? userCredentials = null, CancellationToken cancellationToken = new CancellationToken())
    {
        return new SubscriptionRunner(this, _client.SubscribeToStream(streamName, start, true, userCredentials, cancellationToken));
    }
    public async Task<IAsyncDisposable> SubscribeEventHandler<TEventHandler>(TEventHandler? eh = default, string? outputStream = null,
        FromStream? start = null, bool ensureOutputStreamProjection=true)
        where TEventHandler : class,IEventHandler, ITypeRegister
    {
        return await SubscribeEventHandler<TEventHandler>(_typeHandlerRegister.GetConverterFor<TEventHandler>()!, _typeHandlerRegister.GetEventsFor<TEventHandler>(),
            eh, outputStream, start, ensureOutputStreamProjection);
    }
    public async Task<IAsyncDisposable> SubscribeEventHandler<TEventHandler>(TypeEventConverter mapFunc,
        IEnumerable<string>? eventTypes, TEventHandler? eh = default, string? outputStream = null,
        FromStream? start = null, bool ensureOutputStreamProjection = true)
        where TEventHandler : class, IEventHandler
    {
        eventTypes ??= Array.Empty<string>();
        
        outputStream ??= Conventions.OutputStreamModelConvention(typeof(TEventHandler));
        if (ensureOutputStreamProjection)
            await ProjectionManagementClient.EnsureJoinProjection(outputStream, ProjectionRegister, eventTypes);
        var sub = Subscribe(outputStream, start ?? FromStream.Start);
        if(eh == null)
            await sub.WithHandler<TEventHandler>(mapFunc);
        else
            await sub.WithHandler(eh, mapFunc);
        return sub;
    }
    public async Task<IAsyncDisposable> SubscribeEventHandlerPersistently<TEventHandler>(TypeEventConverter mapFunc,
        IEnumerable<string>? events,
        TEventHandler? model,
        string? outputStream = null, string? groupName = null, IPosition? startFrom = null, bool ensureOutputStreamProjection = true)
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
            await PersistentSubscriptionClient.CreateToStreamAsync(outputStream, groupName, new PersistentSubscriptionSettings(true, startFrom));
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
        string? outputStream = null, string? groupName = null, IPosition? startFrom = null, bool ensureOutputStreamProjection = true)
        where TEventHandler : class,IEventHandler, ITypeRegister =>
        SubscribeEventHandlerPersistently(_typeHandlerRegister.GetConverterFor<TEventHandler>(), _typeHandlerRegister.GetEventsFor<TEventHandler>(),
            model, outputStream, groupName, startFrom, ensureOutputStreamProjection);


    public ISubscriptionRunner SubscribePersistently(string streamName, string groupName, int bufferSize = 10,
        UserCredentials? userCredentials = null, CancellationToken cancellationToken = new CancellationToken())
    {
        return new PersistentSubscriptionRunner(this, _persistentSubscriptionClient.SubscribeToStream(streamName, groupName, bufferSize, userCredentials, cancellationToken));
    }

    public async Task Rehydrate<T>(T model, Guid id) where T : IEventHandler, ITypeRegister
    {
        string streamId = Conventions.GetStreamIdConvention(typeof(T), id);
        await Rehydrate(model, streamId);
    }
    public async Task Rehydrate<T>(T model, string streamId) where T : IEventHandler, ITypeRegister
    {
        
        var items = _client.ReadStreamAsync(Direction.Forwards, streamId, StreamPosition.Start, resolveLinkTos:true);
        var registry = _typeHandlerRegister.GetConverterFor<T>();
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
    public async Task<IEventRecord<TEvent>?> FindEventInStream<TEvent>(string streamId, Guid id, TypeEventConverter? eventMapping = null, Direction scanDirection = Direction.Backwards)
    {
        
        var items = _client.ReadStreamAsync(Direction.Forwards, streamId, StreamPosition.Start);

        eventMapping ??= (string x, out Type tt) =>
        {
            if (x == typeof(TEvent).Name)
            {
                tt = typeof(TEvent);
                return true;
            }

            tt = null;
            return false;
        };
        await foreach (var i in items)
        {
            if (i.Event.EventId.ToGuid() != id) continue;
            if (eventMapping(i.Event.EventType, out var t)) 
                throw new ArgumentException($"We don't know how to deserialize this event: {i.Event.EventType}.");

            var (evt, metadata) = ReadEventData(i.Event, t);
            return new EventRecord<TEvent>() { Event = (TEvent)evt, Metadata = metadata };
        }

        return null;
    }

    public ISubscriptionSet SubscribeSet() => new SubscriptionSet(this);

    public async Task<T> Get<T>(Guid id)
        where T : IAggregate<T>, ITypeRegister
    {
        string streamId = Conventions.GetStreamIdConvention(typeof(T), id);
        
        var items = _client.ReadStreamAsync(Direction.Forwards, streamId, StreamPosition.Start);
        
        var aggregate = T.New(id);
        if (await items.ReadState == ReadState.StreamNotFound) return aggregate;

        var registry = _typeHandlerRegister.GetConverterFor<T>();
        var events = items.Select(x=> new { ResolvedEvent = x, EventType = registry(x.Event.EventType, out var t) ? t : null })
            .Where(x=>x.EventType != null)
            .Select(ev => Serializer.Deserialize(ev.ResolvedEvent.Event.Data.Span, ev.EventType!));
        await aggregate.Rehydrate(events);
        return aggregate;
    }

    public IPlumberConfig Config => this;

    public async Task<IWriteResult> AppendEvents(string streamId, StreamRevision rev, IEnumerable<object> events,
        object? metadata = null)
    {
        var evData = MakeEvents(events, metadata);
        
        return await _client.AppendToStreamAsync(streamId, rev, evData);
    }
    public async Task<IWriteResult> AppendEvents(string streamId, StreamState state, IEnumerable<object> events,
        object? metadata = null)
    {
        var evData = MakeEvents(events, metadata);
        
        var r = await _client.AppendToStreamAsync(streamId, state, evData);
        return r;
    }
    public async Task<IWriteResult> AppendEvent(string streamId, StreamState state, string evtName, object evt, object? metadata = null)
    {
        var m = Conventions.GetMetadata(null, evt, metadata);
        var evId = Conventions.GetEventIdConvention(null, evt);
        var evData = MakeEvent(evId, evtName, evt, m);
        
        var r = await _client.AppendToStreamAsync(streamId, state, [evData]);
        return r;
    }
    /// <summary>
    /// This method is called only from subscriptions.
    /// </summary>
    /// <param name="er"></param>
    /// <param name="t"></param>
    /// <returns></returns>
    internal (object, Metadata) ReadEventData(EventRecord er, Type t)
    {
        var aggregateId = Guid.Parse(er.EventStreamId.Substring(er.EventStreamId.IndexOf('-') + 1));
        var ev = Serializer.Deserialize(er.Data.Span, t)!;
        var m = Serializer.Parse(er.Metadata.Span);
        
        var metadata = new Metadata(aggregateId, er.EventId.ToGuid(), er.EventNumber.ToInt64(), er.EventStreamId, m);
        return (ev, metadata);
        
    }
    private IEnumerable<EventData> MakeEvents(IEnumerable<object> events, object? metadata, IAggregate? agg = null)
    {
        var evData = events.Select(x =>
        {
            var m = Conventions.GetMetadata(agg, x, metadata);
            var evName = this.Conventions.GetEventNameConvention(agg?.GetType(), x.GetType());
            var evId = Conventions.GetEventIdConvention(agg, x);
            return MakeEvent(evId, evName, x, m);
        });
        return evData;
    }

    private EventData MakeEvent(Uuid evId, string evName, object data, object m)
    {
        return new EventData(evId, evName, Serializer.SerializeToUtf8Bytes(data), Serializer.SerializeToUtf8Bytes(m));
    }

    public async Task<IWriteResult> SaveChanges<T>(T aggregate, object? metadata = null)
        where T : IAggregate<T>
    {
        string streamId = Conventions.GetStreamIdConvention(typeof(T), aggregate.Id);
        var evData = MakeEvents(aggregate.PendingEvents, metadata, aggregate);
        EventStoreClient c = new EventStoreClient(_settings);
        var r = await c.AppendToStreamAsync(streamId, StreamRevision.FromInt64(aggregate.Version), evData);
        aggregate.AckCommitted();
        
        return r;
    }


    public async Task<IWriteResult> SaveNew<T>(T aggregate, object? metadata = null)
        where T : IAggregate<T>
    {
        string streamId = Conventions.GetStreamIdConvention(typeof(T), aggregate.Id);
        var evData = MakeEvents(aggregate.PendingEvents, metadata, aggregate);
        EventStoreClient c = new EventStoreClient(_settings);
        var r = await c.AppendToStreamAsync(streamId, StreamState.NoStream, evData);
        aggregate.AckCommitted();
        return r;
    }
    /// <summary>
    /// Appends a link to the stream based on metadata loaded from somewhere else.
    /// </summary>
    /// <param name="streamId">Full name of the stream.</param>
    /// <param name="metadata">Event's metadata that link will point to.</param>
    /// <param name="state">StreamState, default is Any</param>
    /// <returns></returns>
    public async Task<IWriteResult> AppendLink(string streamId, Metadata metadata, StreamState? state = null)
    {
        var data = Encoding.UTF8.GetBytes($"{metadata.SourceStreamPosition}@{metadata.SourceStreamId}");
        const string eventType = "$>";
        
        return await _client.AppendToStreamAsync(streamId, state ?? StreamState.Any,
            new[] { new EventData(Uuid.NewUuid(), eventType, data) });

    }
}