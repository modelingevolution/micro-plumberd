﻿using System.Text.Json;
using EventStore.Client;
using Grpc.Core;

namespace MicroPlumberd;



public class Plumber(EventStoreClientSettings settings) : IPlumber
{
    private readonly EventStoreClient _client = new(settings);
    private readonly EventStorePersistentSubscriptionsClient _persistentSubscriptionClient = new(settings);
    private readonly EventStoreProjectionManagementClient _projectionManagementClient = new (settings);
    private ProjectionRegister? _projectionRegister;
    public IProjectionRegister ProjectionRegister => _projectionRegister ??= new ProjectionRegister(_projectionManagementClient);
    public IObjectSerializer Serializer { get; set; } = new ObjectSerializer();
    public IConventions Conventions { get; } = new Conventions();
    public EventStoreClient Client => _client;
    public EventStorePersistentSubscriptionsClient PersistentSubscriptionClient => _persistentSubscriptionClient;
    public EventStoreProjectionManagementClient ProjectionManagementClient => _projectionManagementClient;

    public ISubscriptionRunner Subscribe(string streamName, FromStream start, 
        UserCredentials? userCredentials = null, CancellationToken cancellationToken = new CancellationToken())
    {
        return new SubscriptionRunner(this,_client.SubscribeToStream(streamName, start, true, userCredentials, cancellationToken));
    }
    public async Task<IAsyncDisposable> SubscribeModel<TModel>(TModel model, FromStream? start = null)
        where TModel : IReadModel, ITypeRegister
    {
        var events = TModel.TypeRegister.Keys;
        var outputStream = typeof(TModel).Name;
        await ProjectionManagementClient.EnsureJoinProjection(outputStream, ProjectionRegister, events);
        var sub = Subscribe(outputStream, start ?? FromStream.Start);
        await sub.WithModel(model);
        return sub;
    }
    public async Task<IAsyncDisposable> SubscribeModelPersistently<TModel>(TModel model)
        where TModel : IReadModel, ITypeRegister
    {
        var events = TModel.TypeRegister.Keys;
        var outputStream = typeof(TModel).Name;
        var groupName = outputStream;
        await ProjectionManagementClient.EnsureJoinProjection(outputStream, ProjectionRegister, events);
        var sub = SubscribePersistently(outputStream, groupName);
        await sub.WithModel(model);
        return sub;
    }
    public ISubscriptionRunner SubscribePersistently(string streamName, string groupName, int bufferSize = 10,
        UserCredentials? userCredentials = null, CancellationToken cancellationToken = new CancellationToken())
    {
        return new PersistentSubscriptionRunner(this, _persistentSubscriptionClient.SubscribeToStream(streamName, groupName, bufferSize, userCredentials, cancellationToken));
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

    public async Task AppendEvents(string streamId, StreamRevision rev, IEnumerable<object> events, object? metadata = null)
    {
        var evData = GetEventData(events, metadata);
        await _client.AppendToStreamAsync(streamId, rev, evData);
    }
    public async Task AppendEvents(string streamId, StreamState state, IEnumerable<object> events, object? metadata = null)
    {
        var evData = GetEventData(events, metadata);
        await _client.AppendToStreamAsync(streamId, state, evData);
    }

    private IEnumerable<EventData> GetEventData(IEnumerable<object> events, object? metadata, IAggregate? agg = null)
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

    public async Task SaveChanges<T>(T aggregate, object? metadata = null)
        where T : IAggregate<T>
    {
        string streamId = Conventions.GetStreamIdConvention(typeof(T), aggregate.Id);
        var evData = GetEventData(aggregate.PendingEvents, metadata, aggregate);
        await _client.AppendToStreamAsync(streamId, StreamRevision.FromInt64(aggregate.Age), evData);
        aggregate.AckCommitted();
    }


    public async Task SaveNew<T>(T aggregate, object? metadata = null)
        where T : IAggregate<T>
    {
        string streamId = Conventions.GetStreamIdConvention(typeof(T), aggregate.Id);
        var evData = GetEventData(aggregate.PendingEvents, metadata, aggregate);
        await _client.AppendToStreamAsync(streamId, StreamState.NoStream, evData);
        aggregate.AckCommitted();
    }

}