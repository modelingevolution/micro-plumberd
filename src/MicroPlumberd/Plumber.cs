using System.Collections.Frozen;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using EventStore.Client;
using Grpc.Core;

namespace MicroPlumberd;


public class Plumber(PlumberEngine engine, OperationContext context) : IPlumber, IPlumberReadOnlyConfig
{
    public static IPlumber Create(EventStoreClientSettings settings, Flow flow = Flow.Component)
    {
        return new Plumber(new PlumberEngine(settings), new OperationContext(flow));
    }
    public IPlumberReadOnlyConfig Config => engine.Config;
    public EventStoreClient Client => engine.Client;
    public EventStorePersistentSubscriptionsClient PersistentSubscriptionClient => engine.PersistentSubscriptionClient;
    public EventStoreProjectionManagementClient ProjectionManagementClient => engine.ProjectionManagementClient;
    public IProjectionRegister ProjectionRegister => engine.ProjectionRegister;
    public ITypeHandlerRegisters TypeHandlerRegisters => engine.TypeHandlerRegisters;

    public Task<IWriteResult> AppendEvents(string streamId, StreamRevision rev, IEnumerable<object> events, object? metadata = null,
        CancellationToken token = default)
    {
        return engine.AppendEvents(context, streamId, rev, events, metadata, token);
    }

    public Task<IWriteResult> AppendEventToStream(string streamId, object evt, StreamState? state = null, string? evtName = null,
        object? metadata = null, CancellationToken token = default)
    {
        return engine.AppendEventToStream(context, streamId, evt, state, evtName, metadata, token);
    }

    public Task<IWriteResult> AppendEvents(string streamId, StreamState state, IEnumerable<object> events, object? metadata = null,
        CancellationToken token = default)
    {
        return engine.AppendEvents(context, streamId, state, events, metadata, token);
    }

    public Task<IEventRecord<T>?> FindEventInStream<T>(string streamId, Guid id, TypeEventConverter eventMapping = null,
        Direction scanDirection = Direction.Backwards, CancellationToken token = default)
    {
        return engine.FindEventInStream<T>(context, streamId, id, eventMapping, scanDirection, token);
    }

    public Task<IEventRecord?> FindEventInStream(string streamId, Guid id, TypeEventConverter eventMapping,
        Direction scanDirection = Direction.Backwards, CancellationToken token = default)
    {
        return engine.FindEventInStream(context, streamId, id, eventMapping, scanDirection, token);
    }

    class SubscriptionSet(OperationContext context, PlumberEngine engine) : ISubscriptionSet
    {
        private IEngineSubscriptionSet _set = new MicroPlumberd.SubscriptionSet(engine);
        public ISubscriptionSet With<TModel>(TModel model) where TModel : IEventHandler, ITypeRegister
        {
            _set = _set.With(model);
            return this;
        }

        public Task SubscribePersistentlyAsync(string outputStream, string? groupName = null) => _set.SubscribePersistentlyAsync(context, outputStream, groupName);

        public Task SubscribeAsync(string name, FromStream start) => _set.SubscribeAsync(context, name, start);
    }
    public ISubscriptionSet SubscribeSet()
    {
        return new SubscriptionSet(context, engine);
    }

    public ISubscriptionRunner Subscribe(string streamName, FromRelativeStreamPosition start,
        UserCredentials? userCredentials = null, CancellationToken cancellationToken = default)
    {
        return engine.Subscribe(streamName, start, userCredentials, cancellationToken);
    }

    public Task<IAsyncDisposable> SubscribeEventHandler<TEventHandler>(TypeEventConverter mapFunc, IEnumerable<string>? eventTypes,
        TEventHandler? eh = default, string? outputStream = null, FromStream? start = null,
        bool ensureOutputStreamProjection = true, CancellationToken ct = default) where TEventHandler : class, IEventHandler
    {
        return engine.SubscribeEventHandler( mapFunc, eventTypes, eh, outputStream, start, ensureOutputStreamProjection, ct);
    }

    public Task<IAsyncDisposable> SubscribeEventHandler<TEventHandler>(TEventHandler? eh = default, string? outputStream = null,
        FromRelativeStreamPosition? start = null, bool ensureOutputStreamProjection = true,
        CancellationToken token = default) where TEventHandler : class, IEventHandler, ITypeRegister
    {
        return engine.SubscribeEventHandler(eh, outputStream, start, ensureOutputStreamProjection, token);
    }

    public Task<IAsyncDisposable> SubscribeEventHandlerPersistently<TEventHandler>(TEventHandler? model,
        string? outputStream = null, string? groupName = null, IPosition? startFrom = null,
        bool ensureOutputStreamProjection = true, int minCheckPointCount = 1, CancellationToken token = default) where TEventHandler : class, IEventHandler, ITypeRegister
    {
        return engine.SubscribeEventHandlerPersistently(model, outputStream, groupName, startFrom,
            ensureOutputStreamProjection, minCheckPointCount, token);
    }

    public ISubscriptionRunner SubscribePersistently(string streamName, string groupName, int bufferSize = 10,
        UserCredentials? userCredentials = null, CancellationToken cancellationToken = new CancellationToken())
    {
        return engine.SubscribePersistently(streamName, groupName, bufferSize, userCredentials, cancellationToken);
    }

    public Task Rehydrate<T>(T model, string stream, StreamPosition? position = null, CancellationToken token = default) 
        where T : IEventHandler, ITypeRegister
    {
        return engine.Rehydrate(context, model, stream, position, token);
    }

    public Task Rehydrate<T>(T model, Guid id, StreamPosition? position = null, CancellationToken token = default) where T : IEventHandler, ITypeRegister
    {
        return engine.Rehydrate(context,model,  id, position, token);
    }

    public Task Rehydrate<T>(T model, string streamId, TypeEventConverter converter, StreamPosition? position = null,
        CancellationToken token = default) where T : IEventHandler
    {
        return engine.Rehydrate(context, model, streamId, converter, position, token);
    }

    public Task<T> Get<T>(object id, CancellationToken token = default) where T : IAggregate<T>, ITypeRegister, IId
    {
        return engine.Get<T>(context, id, token);
    }

    public Task<IWriteResult> SaveChanges<T>(T aggregate, object? metadata = null, CancellationToken token = default) where T : IAggregate<T>, IId
    {
        return engine.SaveChanges(context, aggregate, metadata, token);
    }

    public Task<IWriteResult> SaveNew<T>(T aggregate, object? metadata = null, CancellationToken token = default) where T : IAggregate<T>, IId
    {
        return engine.SaveNew(context, aggregate, metadata, token);
    }

    public Task<Snapshot<T>?> GetSnapshot<T>(Guid id, CancellationToken token = default)
    {
        return engine.GetSnapshot<T>(context, id, token);
    }

    public Task<Snapshot?> GetSnapshot(object id, Type snapshotType, CancellationToken token = default)
    {
        return engine.GetSnapshot(context, id, snapshotType, token);
    }

    public Task<IWriteResult> AppendLink(string streamId, Metadata metadata, StreamState? state = null, CancellationToken token = default) => engine.AppendLink(streamId, metadata, state, token);

    public Task<IAsyncDisposable> SubscribeEventHandlerPersistently<TEventHandler>(TypeEventConverter mapFunc, IEnumerable<string>? events,
        TEventHandler? model, string? outputStream = null, string? groupName = null, IPosition? startFrom = null,
        bool ensureOutputStreamProjection = true, int minCheckPointCount = 1, CancellationToken token = default) where TEventHandler : class, IEventHandler
    {
        return engine.SubscribeEventHandlerPersistently(mapFunc, events, model, outputStream, groupName, startFrom, ensureOutputStreamProjection, minCheckPointCount, token);
    }

    public IAsyncEnumerable<object> Read<TOwner>(object id, StreamPosition? start = null, Direction? direction = null,
        long maxCount = 9223372036854775807, CancellationToken token = default) where TOwner : ITypeRegister
    {
        return engine.Read<TOwner>(context,id, start, direction, maxCount, token);
    }

    public IAsyncEnumerable<object> Read<TOwner>(StreamPosition? start = null, Direction? direction = null,
        long maxCount = 9223372036854775807, CancellationToken token = default) where TOwner : ITypeRegister
    {
        return engine.Read<TOwner>(context, start, direction, maxCount, token);
    }

    public IAsyncEnumerable<object> Read(string streamId, TypeEventConverter converter, StreamPosition? start = null,
        Direction? direction = null, long maxCount = 9223372036854775807, CancellationToken token = default)
    {
        return engine.Read(context, streamId, converter, start, direction, maxCount, token);
    }

    public IAsyncEnumerable<(object, Metadata)> ReadFull(string streamId, TypeEventConverter converter, StreamPosition? start = null,
        Direction? direction = null, long maxCount = 9223372036854775807, CancellationToken token = default)
    {
        return engine.ReadFull(context, streamId, converter, start, direction, maxCount, token);
    }

    public Task<IWriteResult> AppendSnapshot(object snapshot, object id, long version, StreamState? state = null,
        CancellationToken token = default)
    {
        return engine.AppendSnapshot(context, snapshot, id, version, state, token);
    }

    public Task<IWriteResult> AppendEvent(object evt, object? id = null, object? metadata = null, StreamState? state = null,
        string? evtName = null, CancellationToken token = default)
    {
        return engine.AppendEvent(context, evt, id, metadata, state, evtName, token);
    }

    public Task<IWriteResult> AppendLink(string streamId, ulong streamPosition, string streamSourceId, StreamState? state = null,
        CancellationToken token = default)
    {
        return engine.AppendLink(streamId, streamPosition, streamSourceId, state, token);
    }

    public Task<IWriteResult> AppendState(object state, object id, long? version = null, CancellationToken token = default)
    {
        return engine.AppendState(context, state, id, version, token);
    }

    public Task<IWriteResult> AppendState<T>(T state, CancellationToken token = default)
    {
        return engine.AppendState(context, state, token);
    }

    public Task<SubscriptionRunnerState<T>?> GetState<T>(object id, string? streamId = null, CancellationToken token = default) where T : class
    {
        return engine.GetState<T>(context, id, streamId, token);
    }

    public IAsyncEnumerable<(T, Metadata)> ReadEventsOfType<T>(string? streamId = null, StreamPosition? start = null, Direction? direction = null,
        long maxCount = 9223372036854775807, CancellationToken token = default)
    {
        return engine.ReadEventsOfType<T>(context, streamId, start, direction, maxCount, token);
    }

    public Task<IAsyncDisposable> SubscribeStateEventHandler<TEventHandler>(IEnumerable<string>? eventTypes, TEventHandler? eh = default,
        string? outputStream = null, FromRelativeStreamPosition? start = null, bool ensureOutputStreamProjection = true,
        CancellationToken token = default) where TEventHandler : class, IEventHandler, ITypeRegister
    {
        return engine.SubscribeStateEventHandler(eventTypes, eh, outputStream, start, ensureOutputStreamProjection, token);
    }

    public Task<IAsyncDisposable> SubscribeStateEventHandler<TEventHandler>(TEventHandler? eh = default(TEventHandler?), string? outputStream = null,
        FromRelativeStreamPosition? start = null, bool ensureOutputStreamProjection = true,
        CancellationToken token = default) where TEventHandler : class, IEventHandler, ITypeRegister
    {
        return engine.SubscribeStateEventHandler(eh, outputStream, start, ensureOutputStreamProjection, token);
    }

    public Task TryCreateJoinProjection(string outputStream, IEnumerable<string> eventTypes, CancellationToken token = default)
    {
        return engine.TryCreateJoinProjection(outputStream, eventTypes, token);
    }

    public Task TryCreateJoinProjection<TEventHandler>(string? outputStream = null, CancellationToken token = default) where TEventHandler : class, IEventHandler, ITypeRegister
    {
        return engine.TryCreateJoinProjection<TEventHandler>(outputStream, token);
    }

    public Task<IWriteResult> AppendStreamMetadataFromEvent<TEvent>(object id, StreamState? state = null, TimeSpan? maxAge = null,
        StreamPosition? truncateBefore = null, TimeSpan? cacheControl = null, StreamAcl? acl = null, int? maxCount = null)
    {
        return engine.AppendStreamMetadataFromEvent<TEvent>(context, id, state, maxAge, truncateBefore, cacheControl, acl, maxCount);
    }

    public Task<IWriteResult> AppendStreamMetadataFromHandler<THandler>(StreamState? state = null, TimeSpan? maxAge = null,
        StreamPosition? truncateBefore = null, TimeSpan? cacheControl = null, StreamAcl? acl = null, int? maxCount = null)
    {
        return engine.AppendStreamMetadataFromHandler<THandler>(context, state, maxAge, truncateBefore, cacheControl, acl, maxCount);
    }

    public Task<IWriteResult> AppendStreamMetadataFromAggregate<TAggregate>(object id, StreamState? state = null, TimeSpan? maxAge = null,
        StreamPosition? truncateBefore = null, TimeSpan? cacheControl = null, StreamAcl? acl = null, int? maxCount = null)
    {
        return engine.AppendStreamMetadataFromAggregate<TAggregate>(context, id, state, maxAge, truncateBefore, cacheControl, acl, maxCount);
    }

    public Task<IWriteResult> AppendStreamMetadata(string streamId, StreamState? state, TimeSpan? maxAge, StreamPosition? truncateBefore,
        TimeSpan? cacheControl, StreamAcl? acl, int? maxCount)
    {
        return engine.AppendStreamMetadata(context, streamId, state, maxAge, truncateBefore, cacheControl, acl, maxCount);
    }

    public T GetExtension<T>() where T : new()
    {
        return engine.GetExtension<T>();
    }

    public Func<Type, IObjectSerializer> SerializerFactory => engine.SerializerFactory;

    public IReadOnlyConventions Conventions => engine.Conventions;

    public IServiceProvider ServiceProvider => engine.ServiceProvider;

    public Task<ErrorHandleDecision> HandleError(Exception ex, OperationContext context, CancellationToken token)
    {
        return engine.HandleError(ex, context, token);
    }
}

public class PlumberInstance(PlumberEngine engine) : IPlumberInstance, IPlumberReadOnlyConfig
{
    public static IPlumber Create(EventStoreClientSettings settings, Flow flow = Flow.Component)
    {
        return new Plumber(new PlumberEngine(settings), new OperationContext(flow));
    }
    public IPlumberReadOnlyConfig Config => engine.Config;
    public EventStoreClient Client => engine.Client;
    public EventStorePersistentSubscriptionsClient PersistentSubscriptionClient => engine.PersistentSubscriptionClient;
    public EventStoreProjectionManagementClient ProjectionManagementClient => engine.ProjectionManagementClient;
    public IProjectionRegister ProjectionRegister => engine.ProjectionRegister;
    public ITypeHandlerRegisters TypeHandlerRegisters => engine.TypeHandlerRegisters;

    public Task<IWriteResult> AppendEvents(string streamId, StreamRevision rev, IEnumerable<object> events, object? metadata = null,
        CancellationToken token = default)
    {
        using var scope = OperationContext.GetOrCreate(Flow.Request);
        var context = scope.Context;
        return engine.AppendEvents(context, streamId, rev, events, metadata, token);
    }

    public Task<IWriteResult> AppendEventToStream(string streamId, object evt, StreamState? state = null, string? evtName = null,
        object? metadata = null, CancellationToken token = default)
    {
        using var scope = OperationContext.GetOrCreate(Flow.Request);
        var context = scope.Context;
        return engine.AppendEventToStream(context, streamId, evt, state, evtName, metadata, token);
    }

    public Task<IWriteResult> AppendEvents(string streamId, StreamState state, IEnumerable<object> events, object? metadata = null,
        CancellationToken token = default)
    {
        using var scope = OperationContext.GetOrCreate(Flow.Request);
        var context = scope.Context;
        return engine.AppendEvents(context, streamId, state, events, metadata, token);
    }

    public Task<IEventRecord<T>?> FindEventInStream<T>(string streamId, Guid id, TypeEventConverter eventMapping = null,
        Direction scanDirection = Direction.Backwards, CancellationToken token = default)
    {
        using var scope = OperationContext.GetOrCreate(Flow.Request);
        var context = scope.Context;
        return engine.FindEventInStream<T>(context, streamId, id, eventMapping, scanDirection, token);
    }

    public Task<IEventRecord?> FindEventInStream(string streamId, Guid id, TypeEventConverter eventMapping,
        Direction scanDirection = Direction.Backwards, CancellationToken token = default)
    {
        using var scope = OperationContext.GetOrCreate(Flow.Request);
        var context = scope.Context;
        return engine.FindEventInStream(context, streamId, id, eventMapping, scanDirection, token);
    }

    class SubscriptionSet(OperationContext context, PlumberEngine engine) : ISubscriptionSet
    {
        private IEngineSubscriptionSet _set = new MicroPlumberd.SubscriptionSet(engine);
        public ISubscriptionSet With<TModel>(TModel model) where TModel : IEventHandler, ITypeRegister
        {
            _set = _set.With(model);
            return this;
        }

        public Task SubscribePersistentlyAsync(string outputStream, string? groupName = null) => _set.SubscribePersistentlyAsync(context, outputStream, groupName);

        public Task SubscribeAsync(string name, FromStream start) => _set.SubscribeAsync(context, name, start);
    }
    public ISubscriptionSet SubscribeSet()
    {
        var scope = OperationContext.GetOrCreate(Flow.Request);
        var context = scope.Context;
        return new SubscriptionSet(context, engine);
    }

    public ISubscriptionRunner Subscribe(string streamName, FromRelativeStreamPosition start,
        UserCredentials? userCredentials = null, CancellationToken cancellationToken = default)
    {
        return engine.Subscribe(streamName, start, userCredentials, cancellationToken);
    }

    public Task<IAsyncDisposable> SubscribeEventHandler<TEventHandler>(TypeEventConverter mapFunc, IEnumerable<string>? eventTypes,
        TEventHandler? eh = default, string? outputStream = null, FromStream? start = null,
        bool ensureOutputStreamProjection = true, CancellationToken ct = default) where TEventHandler : class, IEventHandler
    {
        return engine.SubscribeEventHandler(mapFunc, eventTypes, eh, outputStream, start, ensureOutputStreamProjection, ct);
    }

    public Task<IAsyncDisposable> SubscribeEventHandler<TEventHandler>(TEventHandler? eh = default, string? outputStream = null,
        FromRelativeStreamPosition? start = null, bool ensureOutputStreamProjection = true,
        CancellationToken token = default) where TEventHandler : class, IEventHandler, ITypeRegister
    {
        return engine.SubscribeEventHandler(eh, outputStream, start, ensureOutputStreamProjection, token);
    }

    public Task<IAsyncDisposable> SubscribeEventHandlerPersistently<TEventHandler>(TEventHandler? model = default(TEventHandler?),
        string? outputStream = null, string? groupName = null, IPosition? startFrom = null,
        bool ensureOutputStreamProjection = true, int minCheckPointCount = 1, CancellationToken token = default) where TEventHandler : class, IEventHandler, ITypeRegister
    {
        return engine.SubscribeEventHandlerPersistently(model, outputStream, groupName, startFrom,
            ensureOutputStreamProjection, minCheckPointCount, token);
    }

    public ISubscriptionRunner SubscribePersistently(string streamName, string groupName, int bufferSize = 10,
        UserCredentials? userCredentials = null, CancellationToken cancellationToken = new CancellationToken())
    {
        return engine.SubscribePersistently(streamName, groupName, bufferSize, userCredentials, cancellationToken);
    }

    public Task Rehydrate<T>(T model, string stream, StreamPosition? position = null, CancellationToken token = default)
        where T : IEventHandler, ITypeRegister
    {
        using var scope = OperationContext.GetOrCreate(Flow.Request);
        var context = scope.Context;
        return engine.Rehydrate(context, model, stream, position, token);
    }

    public Task Rehydrate<T>(T model, Guid id, StreamPosition? position = null, CancellationToken token = default) where T : IEventHandler, ITypeRegister
    {
        using var scope = OperationContext.GetOrCreate(Flow.Request);
        var context = scope.Context;
        return engine.Rehydrate(context, model, id, position, token);
    }

    public Task Rehydrate<T>(T model, string streamId, TypeEventConverter converter, StreamPosition? position = null,
        CancellationToken token = default) where T : IEventHandler
    {
        using var scope = OperationContext.GetOrCreate(Flow.Request);
        var context = scope.Context;
        return engine.Rehydrate(context, model, streamId, converter, position, token);
    }

    public Task<T> Get<T>(object id, CancellationToken token = default) where T : IAggregate<T>, ITypeRegister, IId
    {
        using var scope = OperationContext.GetOrCreate(Flow.Request);
        var context = scope.Context;
        return engine.Get<T>(context, id, token);
    }

    public Task<IWriteResult> SaveChanges<T>(T aggregate, object? metadata = null, CancellationToken token = default) where T : IAggregate<T>, IId
    {
        using var scope = OperationContext.GetOrCreate(Flow.Request);
        var context = scope.Context;
        return engine.SaveChanges(context, aggregate, metadata, token);
    }

    public Task<IWriteResult> SaveNew<T>(T aggregate, object? metadata = null, CancellationToken token = default) where T : IAggregate<T>, IId
    {
        using var scope = OperationContext.GetOrCreate(Flow.Request);
        var context = scope.Context;
        return engine.SaveNew(context, aggregate, metadata, token);
    }

    public Task<Snapshot<T>?> GetSnapshot<T>(Guid id, CancellationToken token = default)
    {
        using var scope = OperationContext.GetOrCreate(Flow.Request);
        var context = scope.Context;
        return engine.GetSnapshot<T>(context, id, token);
    }

    public Task<Snapshot?> GetSnapshot(object id, Type snapshotType, CancellationToken token = default)
    {
        using var scope = OperationContext.GetOrCreate(Flow.Request);
        var context = scope.Context;
        return engine.GetSnapshot(context, id, snapshotType, token);
    }

    public Task<IWriteResult> AppendLink(string streamId, Metadata metadata, StreamState? state = null, CancellationToken token = default) => engine.AppendLink(streamId, metadata, state, token);

    public Task<IAsyncDisposable> SubscribeEventHandlerPersistently<TEventHandler>(TypeEventConverter mapFunc, IEnumerable<string>? events,
        TEventHandler? model, string? outputStream = null, string? groupName = null, IPosition? startFrom = null,
        bool ensureOutputStreamProjection = true, int minCheckPointCount = 1, CancellationToken token = default) where TEventHandler : class, IEventHandler
    {
        return engine.SubscribeEventHandlerPersistently(mapFunc, events, model, outputStream, groupName, startFrom, ensureOutputStreamProjection, minCheckPointCount, token);
    }

    public async IAsyncEnumerable<object> Read<TOwner>(object id, StreamPosition? start = null, Direction? direction = null,
        long maxCount = 9223372036854775807, CancellationToken token = default) where TOwner : ITypeRegister
    {
        using var scope = OperationContext.GetOrCreate(Flow.Request);
        var context = scope.Context;
        await foreach (var i in engine.Read<TOwner>(context, id, start, direction, maxCount, token))
        {
            yield return i;
        }
    }

    public async IAsyncEnumerable<object> Read<TOwner>(StreamPosition? start = null, Direction? direction = null,
        long maxCount = 9223372036854775807, CancellationToken token = default) where TOwner : ITypeRegister
    {
        using var scope = OperationContext.GetOrCreate(Flow.Request);
        var context = scope.Context;
        await foreach (var i in engine.Read<TOwner>(context, start, direction, maxCount, token))
        {
            yield return i;
        }
    }

    public async IAsyncEnumerable<object> Read(string streamId, TypeEventConverter converter, StreamPosition? start = null,
        Direction? direction = null, long maxCount = 9223372036854775807, CancellationToken token = default)
    {
        using var scope = OperationContext.GetOrCreate(Flow.Request);
        var context = scope.Context;
        await foreach (var i in engine.Read(context, streamId, converter, start, direction, maxCount, token))
        {
            yield return i;
        }
    }

    public async IAsyncEnumerable<(object, Metadata)> ReadFull(string streamId, TypeEventConverter converter, StreamPosition? start = null,
        Direction? direction = null, long maxCount = 9223372036854775807, CancellationToken token = default)
    {
        using var scope = OperationContext.GetOrCreate(Flow.Request);
        var context = scope.Context;
        await foreach (var i in engine.ReadFull(context, streamId, converter, start, direction, maxCount, token))
        {
            yield return i;
        }
    }

    public Task<IWriteResult> AppendSnapshot(object snapshot, object id, long version, StreamState? state = null,
        CancellationToken token = default)
    {
        using var scope = OperationContext.GetOrCreate(Flow.Request);
        var context = scope.Context;
        return engine.AppendSnapshot(context, snapshot, id, version, state, token);
    }

    public Task<IWriteResult> AppendEvent(object evt, object? id = null, object? metadata = null, StreamState? state = null,
        string? evtName = null, CancellationToken token = default)
    {
        using var scope = OperationContext.GetOrCreate(Flow.Request);
        var context = scope.Context;
        return engine.AppendEvent(context, evt, id, metadata, state, evtName, token);
    }

    public Task<IWriteResult> AppendLink(string streamId, ulong streamPosition, string streamSourceId, StreamState? state = null,
        CancellationToken token = default)
    {
        return engine.AppendLink(streamId, streamPosition, streamSourceId, state, token);
    }

    public Task<IWriteResult> AppendState(object state, object id, long? version = null, CancellationToken token = default)
    {
        using var scope = OperationContext.GetOrCreate(Flow.Request);
        var context = scope.Context;
        return engine.AppendState(context, state, id, version, token);
    }

    public Task<IWriteResult> AppendState<T>(T state, CancellationToken token = default)
    {
        using var scope = OperationContext.GetOrCreate(Flow.Request);
        var context = scope.Context;
        return engine.AppendState(context, state, token);
    }

    public Task<SubscriptionRunnerState<T>?> GetState<T>(object id, string? streamId = null, CancellationToken token = default) where T : class
    {
        using var scope = OperationContext.GetOrCreate(Flow.Request);
        var context = scope.Context;
        return engine.GetState<T>(context, id, streamId, token);
    }

    public IAsyncEnumerable<(T, Metadata)> ReadEventsOfType<T>(string? streamId = null, StreamPosition? start = null, Direction? direction = null,
        long maxCount = 9223372036854775807, CancellationToken token = default)
    {
        using var scope = OperationContext.GetOrCreate(Flow.Request);
        var context = scope.Context;
        return engine.ReadEventsOfType<T>(context, streamId, start, direction, maxCount, token);
    }

    public Task<IAsyncDisposable> SubscribeStateEventHandler<TEventHandler>(IEnumerable<string>? eventTypes, TEventHandler? eh = default,
        string? outputStream = null, FromRelativeStreamPosition? start = null, bool ensureOutputStreamProjection = true,
        CancellationToken token = default) where TEventHandler : class, IEventHandler, ITypeRegister
    {
        return engine.SubscribeStateEventHandler(eventTypes, eh, outputStream, start, ensureOutputStreamProjection, token);
    }

    public Task<IAsyncDisposable> SubscribeStateEventHandler<TEventHandler>(TEventHandler? eh = default(TEventHandler?), string? outputStream = null,
        FromRelativeStreamPosition? start = null, bool ensureOutputStreamProjection = true,
        CancellationToken token = default) where TEventHandler : class, IEventHandler, ITypeRegister
    {
        return engine.SubscribeStateEventHandler(eh, outputStream, start, ensureOutputStreamProjection, token);
    }

    public Task TryCreateJoinProjection(string outputStream, IEnumerable<string> eventTypes, CancellationToken token = default)
    {
        return engine.TryCreateJoinProjection(outputStream, eventTypes, token);
    }

    public Task TryCreateJoinProjection<TEventHandler>(string? outputStream = null, CancellationToken token = default) where TEventHandler : class, IEventHandler, ITypeRegister
    {
        return engine.TryCreateJoinProjection<TEventHandler>(outputStream, token);
    }

    public Task<IWriteResult> AppendStreamMetadataFromEvent<TEvent>(object id, StreamState? state = null, TimeSpan? maxAge = null,
        StreamPosition? truncateBefore = null, TimeSpan? cacheControl = null, StreamAcl? acl = null, int? maxCount = null)
    {
        using var scope = OperationContext.GetOrCreate(Flow.Request);
        var context = scope.Context;
        return engine.AppendStreamMetadataFromEvent<TEvent>(context, id, state, maxAge, truncateBefore, cacheControl, acl, maxCount);
    }

    public Task<IWriteResult> AppendStreamMetadataFromHandler<THandler>(StreamState? state = null, TimeSpan? maxAge = null,
        StreamPosition? truncateBefore = null, TimeSpan? cacheControl = null, StreamAcl? acl = null, int? maxCount = null)
    {
        using var scope = OperationContext.GetOrCreate(Flow.Request);
        var context = scope.Context;
        return engine.AppendStreamMetadataFromHandler<THandler>(context, state, maxAge, truncateBefore, cacheControl, acl, maxCount);
    }

    public Task<IWriteResult> AppendStreamMetadataFromAggregate<TAggregate>(object id, StreamState? state = null, TimeSpan? maxAge = null,
        StreamPosition? truncateBefore = null, TimeSpan? cacheControl = null, StreamAcl? acl = null, int? maxCount = null)
    {
        using var scope = OperationContext.GetOrCreate(Flow.Request);
        var context = scope.Context;
        return engine.AppendStreamMetadataFromAggregate<TAggregate>(context, id, state, maxAge, truncateBefore, cacheControl, acl, maxCount);
    }

    public Task<IWriteResult> AppendStreamMetadata(string streamId, StreamState? state, TimeSpan? maxAge, StreamPosition? truncateBefore,
        TimeSpan? cacheControl, StreamAcl? acl, int? maxCount)
    {
        using var scope = OperationContext.GetOrCreate(Flow.Request);
        var context = scope.Context;
        return engine.AppendStreamMetadata(context, streamId, state, maxAge, truncateBefore, cacheControl, acl, maxCount);
    }

    public T GetExtension<T>() where T : new()
    {
        return engine.GetExtension<T>();
    }

    public Func<Type, IObjectSerializer> SerializerFactory => engine.SerializerFactory;

    public IReadOnlyConventions Conventions => engine.Conventions;

    public IServiceProvider ServiceProvider => engine.ServiceProvider;

    public Task<ErrorHandleDecision> HandleError(Exception ex, OperationContext context, CancellationToken token)
    {
        return engine.HandleError(ex, context, token);
    }
}