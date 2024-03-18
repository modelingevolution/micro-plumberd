﻿
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json;
using EventStore.Client;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;

namespace MicroPlumberd;

public interface IPlumberConfig
{
    IObjectSerializer Serializer { get; set; }
    IConventions Conventions { get; }
    IServiceProvider ServiceProvider { get; set; }
}

class PlumberConfig : IPlumberConfig
{
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
public class Plumber : IPlumber, IPlumberConfig
{
    private readonly EventStoreClient _client;
    private readonly EventStorePersistentSubscriptionsClient _persistentSubscriptionClient;
    private readonly EventStoreProjectionManagementClient _projectionManagementClient;
    
    public static IPlumber Create(EventStoreClientSettings? settings = null, Action<IPlumberConfig>? configure = null)
    {
        settings ??= EventStoreClientSettings.Create($"esdb://admin:changeit@localhost:2113?tls=false&tlsVerifyCert=false");
        var cfg = new PlumberConfig();
        configure?.Invoke(cfg);
        return new Plumber(settings, cfg);
    }
    internal Plumber(EventStoreClientSettings settings, IPlumberConfig? config = null)
    {
        config ??= new PlumberConfig();
        _client = new(settings);
        _persistentSubscriptionClient = new(settings);
        _projectionManagementClient = new (settings);
        this.Conventions = config.Conventions;
        this.Serializer = config.Serializer;
        this.ServiceProvider = config.ServiceProvider;
    }
    public IServiceProvider ServiceProvider { get; set; }
    public IObjectSerializer Serializer { get; set; }
    public IConventions Conventions { get; }
    private ProjectionRegister? _projectionRegister;
    public IProjectionRegister ProjectionRegister => _projectionRegister ??= new ProjectionRegister(_projectionManagementClient);
    public EventStoreClient Client => _client;
    public EventStorePersistentSubscriptionsClient PersistentSubscriptionClient => _persistentSubscriptionClient;
    public EventStoreProjectionManagementClient ProjectionManagementClient => _projectionManagementClient;

    public ISubscriptionRunner Subscribe(string streamName, FromStream start, 
        UserCredentials? userCredentials = null, CancellationToken cancellationToken = new CancellationToken())
    {
        return new SubscriptionRunner(this,_client.SubscribeToStream(streamName, start, true, userCredentials, cancellationToken));
    }
    public async Task<IAsyncDisposable> SubscribeEventHandle<TEventHandler>(TEventHandler? eh = default, string? outputStream = null,
        FromStream? start = null)
        where TEventHandler : class,IEventHandler, ITypeRegister
    {
        eh ??= this.ServiceProvider.GetRequiredService<TEventHandler>();
        var events = TEventHandler.TypeRegister.Keys;
        outputStream ??= Conventions.OutputStreamModelConvention(typeof(TEventHandler));
        await ProjectionManagementClient.EnsureJoinProjection(outputStream, ProjectionRegister, events);
        var sub = Subscribe(outputStream, start ?? FromStream.Start);
        await sub.WithHandler(eh);
        return sub;
    }
    public async Task<IAsyncDisposable> SubscribeEventHandlerPersistently<TEventHandler>(TEventHandler? model,
        string? outputStream = null, string? groupName = null, IPosition? startFrom = null)
        where TEventHandler : class,IEventHandler, ITypeRegister
    {
        model ??= this.ServiceProvider.GetRequiredService<TEventHandler>();
        var events = TEventHandler.TypeRegister.Keys;
        var handlerType = typeof(TEventHandler);
        startFrom ??= StreamPosition.End;
        outputStream ??= Conventions.OutputStreamModelConvention(handlerType);
        groupName ??= Conventions.GroupNameModelConvention(handlerType);
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
        await sub.WithHandler(model);
        return sub;
    }
   

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
        var items = _client.ReadStreamAsync(Direction.Forwards, streamId, StreamPosition.Start);
        var registry = T.TypeRegister;
        var vAware = model as IVersionAware;
        await foreach (var i in items)
        {
            if (!registry.TryGetValue(i.Event.EventType, out var t))
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
    public async Task<IEventRecord<TEvent>?> FindEventInStream<TEvent>(string streamId, Guid id, TypeEventConverter eventMapping = null, Direction scanDirection = Direction.Backwards)
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
        var registry = T.TypeRegister;
        var events = items.Select(ev => Serializer.Deserialize(ev.Event.Data.Span, registry[ev.Event.EventType])!);

        var aggregate = T.New(id);
        await aggregate.Rehydrate(events);
        return aggregate;
    }

    public IPlumberConfig Config => this;

    public async Task<IWriteResult> AppendEvents(string streamId, StreamRevision rev, IEnumerable<object> events,
        object? metadata = null)
    {
        var evData = MakeEventData(events, metadata);
        return await _client.AppendToStreamAsync(streamId, rev, evData);
    }
    public async Task<IWriteResult> AppendEvents(string streamId, StreamState state, IEnumerable<object> events,
        object? metadata = null)
    {
        var evData = MakeEventData(events, metadata);
        var r = await _client.AppendToStreamAsync(streamId, state, evData);
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
    private IEnumerable<EventData> MakeEventData(IEnumerable<object> events, object? metadata, IAggregate? agg = null)
    {
        var evData = events.Select(x =>
        {
            var m = Conventions.GetMetadata(agg, x, metadata);
            var evName = this.Conventions.GetEventNameConvention(agg, x);
            var evId = Conventions.GetEventIdConvention(agg, x);
            return new EventData(evId, evName, Serializer.SerializeToUtf8Bytes(x),
                Serializer.SerializeToUtf8Bytes(m));
        });
        return evData;
    }

    public async Task<IWriteResult> SaveChanges<T>(T aggregate, object? metadata = null)
        where T : IAggregate<T>
    {
        string streamId = Conventions.GetStreamIdConvention(typeof(T), aggregate.Id);
        var evData = MakeEventData(aggregate.PendingEvents, metadata, aggregate);
        var r = await _client.AppendToStreamAsync(streamId, StreamRevision.FromInt64(aggregate.Version), evData);
        aggregate.AckCommitted();
        return r;
    }


    public async Task<IWriteResult> SaveNew<T>(T aggregate, object? metadata = null)
        where T : IAggregate<T>
    {
        string streamId = Conventions.GetStreamIdConvention(typeof(T), aggregate.Id);
        var evData = MakeEventData(aggregate.PendingEvents, metadata, aggregate);
        var r = await _client.AppendToStreamAsync(streamId, StreamState.NoStream, evData);
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