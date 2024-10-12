using EventStore.Client;
using LiteDB;
using LiteDB.Engine;
using System.Xml;

namespace MicroPlumberd.Services.LiteDb
{
    public class PayloadData<TPayload> 
    {
        public Guid Id { get; set; }
        public TPayload Payload { get; set; }
    }

    public class Event
    {
        public long Id { get; set; }
        public Guid EventId { get; set; }
        public string EventName { get; set; }
        public long StreamPosition { get; set; }

        public string StreamFullName { get; set; }
        
        public string StreamId { get; set; }
        public string StreamCategory { get; set; }
        public IDictionary<string, string> Metadata { get; set; }
    }

    public class Stream
    {
        public Guid Id { get; set; }
        public string Category { get; set; }
        public string FullName { get; set; }

        public long Position { get; set; }
    }

    

    public class LiteEventStore
    {
        public interface IPlayloadEvents
        {
            Guid Insert(object evt, Guid evtId);
        }
        interface IGenericCollectionFactory
        {
            IPlayloadEvents Create(LiteDatabase db, string name);
        }

        class PayloadsFactory<T> : IGenericCollectionFactory
        {
            public IPlayloadEvents Create(LiteDatabase db, string name)
            {
                return new PayloadCollection<T>(db.GetCollection<PayloadData<T>>(name));
            }
        }
        class PayloadCollection<T> : IPlayloadEvents
        {
            public ILiteCollection<PayloadData<T>> Collection { get; }
            public PayloadCollection(ILiteCollection<PayloadData<T>> collection)
            {
                Collection = collection;
            }


            public Guid Insert(object evt, Guid evtId)
            {
                return Collection.Insert(new PayloadData<T>() { Payload = (T)evt, Id = evtId}).AsGuid;
            }
        }
        private readonly LiteDatabase _storage;
        public bool BeginTrans() => _storage.BeginTrans();

        public bool Commit() => _storage.Commit();

        public bool Rollback() => _storage.Rollback();

        public LiteEventStore(LiteDatabase storage)
        {
            _storage = storage;
            var mapper = BsonMapper.Global;

            mapper.Entity<Event>()
                .Id(x => x.Id,true);

            
            
        }

        public IPlayloadEvents GetCollection(Type t, string name)
        {
            var ct= typeof(PayloadsFactory<>).MakeGenericType(t);
            var factory = (IGenericCollectionFactory)Activator.CreateInstance(ct);
            return factory.Create(_storage, name);
        }
        public ILiteCollection<PayloadData<TPayload>> Payloads<TPayload>(string name) => _storage.GetCollection<PayloadData<TPayload>>(name);
        public ILiteCollection<Event> Events => _storage.GetCollection<Event>("events");
        public ILiteCollection<Stream> Streams => _storage.GetCollection<Stream>("streams");

        public Guid InsertPayload(object evt, string evtName, Uuid evId)
        {
            var evts = this.GetCollection(evt.GetType(), evtName);
            return evts.Insert(evt, evId.ToGuid());
        }
    }


    public class LiteDbPlumber(LiteEventStore storage) : IPlumber
    {
        public IPlumberReadOnlyConfig Config { get; }
        public EventStoreClient Client { get; }
        public EventStorePersistentSubscriptionsClient PersistentSubscriptionClient { get; }
        public EventStoreProjectionManagementClient ProjectionManagementClient { get; }
        public IProjectionRegister ProjectionRegister { get; }
        public ITypeHandlerRegisters TypeHandlerRegisters { get; }
        public async Task<IWriteResult> AppendEvent(object evt, object? id = null, object? metadata = null, StreamState? state = null, string? evtName = null, CancellationToken token = default)
        {
            if (evt == null) throw new ArgumentException("evt cannot be null.");


            evtName ??= Config.Conventions.GetEventNameConvention(null, evt.GetType());
            var m = Config.Conventions.GetMetadata(null, evt, metadata);
            var st = state ?? StreamState.Any;
            var streamId = Config.Conventions.StreamNameFromEventConvention(evt.GetType(), id);
            var evId = Config.Conventions.GetEventIdConvention(null, evt);

            storage.BeginTrans();
            var stream = storage.Streams.FindOne(x => x.FullName == streamId);
            stream.Position += 1;
            storage.Streams.Update(stream);
            
            var evtId = storage.InsertPayload(evt, evtName, evId);
            storage.Events.Insert(new Event()
            {
                EventId = evtId,
                EventName = evtName,
                StreamId = id?.ToString() ?? string.Empty,
                StreamFullName = streamId,
                StreamPosition = stream.Position,
            });

            storage.Commit();
            return null;
        }

        public ISubscriptionSet SubscribeSet()
        {
            throw new NotImplementedException();
        }

        public async Task<IAsyncDisposable> SubscribeEventHandler<TEventHandler>(TEventHandler? eh = default, string? outputStream = null,
            FromRelativeStreamPosition? start = null, bool ensureOutputStreamProjection = true,
            CancellationToken token = default) where TEventHandler : class, IEventHandler, ITypeRegister
        {
            throw new NotImplementedException();
        }

        public async Task<T> Get<T>(object id, CancellationToken token = default) where T : IAggregate<T>, ITypeRegister, IId
        {
            throw new NotImplementedException();
        }

        public async Task<IWriteResult> SaveChanges<T>(T aggregate, object? metadata = null, CancellationToken token = default) where T : IAggregate<T>, IId
        {
            throw new NotImplementedException();
        }

        public async Task<IWriteResult> SaveNew<T>(T aggregate, object? metadata = null, CancellationToken token = default) where T : IAggregate<T>, IId
        {
            throw new NotImplementedException();
        }

        public async Task<Snapshot<T>?> GetSnapshot<T>(Guid id, CancellationToken token = default)
        {
            throw new NotImplementedException();
        }

        public async Task<Snapshot?> GetSnapshot(object id, Type snapshotType, CancellationToken token = default)
        {
            throw new NotImplementedException();
        }

        public async Task<IWriteResult> AppendState(object state, object id, long? version = null, CancellationToken token = default)
        {
            throw new NotImplementedException();
        }

        public async Task<IWriteResult> AppendState<T>(T state, CancellationToken token = default)
        {
            throw new NotImplementedException();
        }

        public async Task<SubscriptionRunnerState<T>?> GetState<T>(object id, string? streamId = null, CancellationToken token = default) where T : class
        {
            throw new NotImplementedException();
        }

        public IAsyncEnumerable<(T, Metadata)> ReadEventsOfType<T>(string? streamId = null, StreamPosition? start = null, Direction? direction = null,
            long maxCount = 9223372036854775807, CancellationToken token = default)
        {
            throw new NotImplementedException();
        }

        public async Task<IAsyncDisposable> SubscribeStateEventHandler<TEventHandler>(IEnumerable<string>? eventTypes, TEventHandler? eh = default,
            string? outputStream = null, FromRelativeStreamPosition? start = null, bool ensureOutputStreamProjection = true,
            CancellationToken token = default) where TEventHandler : class, IEventHandler, ITypeRegister
        {
            throw new NotImplementedException();
        }

        public async Task<IAsyncDisposable> SubscribeStateEventHandler<TEventHandler>(TEventHandler? eh = default(TEventHandler?), string? outputStream = null,
            FromRelativeStreamPosition? start = null, bool ensureOutputStreamProjection = true,
            CancellationToken token = default) where TEventHandler : class, IEventHandler, ITypeRegister
        {
            throw new NotImplementedException();
        }

        public async Task<IWriteResult> AppendLink(string streamId, ulong streamPosition, string streamSourceId, StreamState? state = null,
            CancellationToken token = default)
        {
            throw new NotImplementedException();
        }

        

        public async Task<IWriteResult> AppendSnapshot(object snapshot, object id, long version, StreamState? state = null,
            CancellationToken token = default)
        {
            throw new NotImplementedException();
        }

        public IAsyncEnumerable<(object, Metadata)> ReadFull(string streamId, TypeEventConverter converter, StreamPosition? start = null,
            Direction? direction = null, long maxCount = 9223372036854775807, CancellationToken token = default)
        {
            throw new NotImplementedException();
        }

        public IAsyncEnumerable<object> Read(string streamId, TypeEventConverter converter, StreamPosition? start = null,
            Direction? direction = null, long maxCount = 9223372036854775807, CancellationToken token = default)
        {
            throw new NotImplementedException();
        }

        public IAsyncEnumerable<object> Read<TOwner>(StreamPosition? start = null, Direction? direction = null,
            long maxCount = 9223372036854775807, CancellationToken token = default) where TOwner : ITypeRegister
        {
            throw new NotImplementedException();
        }

        public IAsyncEnumerable<object> Read<TOwner>(object id, StreamPosition? start = null, Direction? direction = null,
            long maxCount = 9223372036854775807, CancellationToken token = default) where TOwner : ITypeRegister
        {
            throw new NotImplementedException();
        }

        public async Task<IAsyncDisposable> SubscribeEventHandlerPersistently<TEventHandler>(TypeEventConverter mapFunc, IEnumerable<string>? events,
            TEventHandler? model, string? outputStream = null, string? groupName = null, IPosition? startFrom = null,
            bool ensureOutputStreamProjection = true, CancellationToken token = default) where TEventHandler : class, IEventHandler
        {
            throw new NotImplementedException();
        }

        public async Task<IWriteResult> AppendLink(string streamId, Metadata metadata, StreamState? state = null, CancellationToken token = default)
        {
            throw new NotImplementedException();
        }

        public async Task Rehydrate<T>(T model, Guid id, StreamPosition? position = null, CancellationToken token = default) where T : IEventHandler, ITypeRegister
        {
            throw new NotImplementedException();
        }

        public async Task Rehydrate<T>(T model, string stream, StreamPosition? position = null, CancellationToken token = default) where T : IEventHandler, ITypeRegister
        {
            throw new NotImplementedException();
        }

        public ISubscriptionRunner SubscribePersistently(string streamName, string groupName, int bufferSize = 10,
            UserCredentials? userCredentials = null, CancellationToken cancellationToken = new CancellationToken())
        {
            throw new NotImplementedException();
        }

        public async Task<IAsyncDisposable> SubscribeEventHandlerPersistently<TEventHandler>(TEventHandler? model = default(TEventHandler?),
            string? outputStream = null, string? groupName = null, IPosition? startFrom = null,
            bool ensureOutputStreamProjection = true, CancellationToken token = default) where TEventHandler : class, IEventHandler, ITypeRegister
        {
            throw new NotImplementedException();
        }

        public async Task<IAsyncDisposable> SubscribeEventHandler<TEventHandler>(TypeEventConverter mapFunc, IEnumerable<string>? eventTypes,
            TEventHandler? eh = default, string? outputStream = null, FromStream? start = null,
            bool ensureOutputStreamProjection = true) where TEventHandler : class, IEventHandler
        {
            throw new NotImplementedException();
        }

        public ISubscriptionRunner Subscribe(string streamName, FromRelativeStreamPosition start,
            UserCredentials? userCredentials = null, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public async Task<IEventRecord?> FindEventInStream(string streamId, Guid id, TypeEventConverter eventMapping,
            Direction scanDirection = Direction.Backwards, CancellationToken token = default)
        {
            throw new NotImplementedException();
        }

        public async Task<IEventRecord<T>?> FindEventInStream<T>(string streamId, Guid id, TypeEventConverter eventMapping = null,
            Direction scanDirection = Direction.Backwards, CancellationToken token = default)
        {
            throw new NotImplementedException();
        }

        public async Task<IWriteResult> AppendEvents(string streamId, StreamState state, IEnumerable<object> events, object? metadata = null,
            CancellationToken token = default)
        {
            throw new NotImplementedException();
        }

        public async Task<IWriteResult> AppendEventToStream(string streamId, object evt, StreamState? state = null, string? evtName = null,
            object? metadata = null, CancellationToken token = default)
        {
            throw new NotImplementedException();
        }

        public async Task<IWriteResult> AppendEvents(string streamId, StreamRevision rev, IEnumerable<object> events, object? metadata = null,
            CancellationToken token = default)
        {
            throw new NotImplementedException();
        }
    }
}
