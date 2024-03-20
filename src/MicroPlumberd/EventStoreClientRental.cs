using EventStore.Client;

namespace MicroPlumberd;

public interface IEventStoreClientRental : IDisposable
{
    string ConnectionName { get; }

    Task<IWriteResult> AppendToStreamAsync(string streamName, StreamRevision expectedRevision, IEnumerable<EventData> eventData,
        Action<EventStoreClientOperationOptions>? configureOperationOptions = null, TimeSpan? deadline = null, UserCredentials? userCredentials = null,
        CancellationToken cancellationToken = new CancellationToken());

    Task<IWriteResult> AppendToStreamAsync(string streamName, StreamState expectedState, IEnumerable<EventData> eventData,
        Action<EventStoreClientOperationOptions>? configureOperationOptions = null, TimeSpan? deadline = null, UserCredentials? userCredentials = null,
        CancellationToken cancellationToken = new CancellationToken());

    Task<DeleteResult> DeleteAsync(string streamName, StreamRevision expectedRevision, TimeSpan? deadline = null,
        UserCredentials? userCredentials = null, CancellationToken cancellationToken = new CancellationToken());

    Task<DeleteResult> DeleteAsync(string streamName, StreamState expectedState, TimeSpan? deadline = null,
        UserCredentials? userCredentials = null, CancellationToken cancellationToken = new CancellationToken());

    Task<StreamMetadataResult> GetStreamMetadataAsync(string streamName, TimeSpan? deadline = null, UserCredentials? userCredentials = null,
        CancellationToken cancellationToken = new CancellationToken());

    Task<IWriteResult> SetStreamMetadataAsync(string streamName, StreamState expectedState, StreamMetadata metadata,
        Action<EventStoreClientOperationOptions>? configureOperationOptions = null, TimeSpan? deadline = null, UserCredentials? userCredentials = null,
        CancellationToken cancellationToken = new CancellationToken());

    Task<IWriteResult> SetStreamMetadataAsync(string streamName, StreamRevision expectedRevision, StreamMetadata metadata,
        Action<EventStoreClientOperationOptions>? configureOperationOptions = null, TimeSpan? deadline = null, UserCredentials? userCredentials = null,
        CancellationToken cancellationToken = new CancellationToken());

    EventStoreClient.ReadAllStreamResult ReadAllAsync(Direction direction, Position position, long maxCount = 9223372036854775807,
        bool resolveLinkTos = false, TimeSpan? deadline = null, UserCredentials? userCredentials = null,
        CancellationToken cancellationToken = new CancellationToken());

    EventStoreClient.ReadAllStreamResult ReadAllAsync(Direction direction, Position position, IEventFilter eventFilter,
        long maxCount = 9223372036854775807, bool resolveLinkTos = false, TimeSpan? deadline = null,
        UserCredentials? userCredentials = null, CancellationToken cancellationToken = new CancellationToken());

    EventStoreClient.ReadStreamResult ReadStreamAsync(Direction direction, string streamName, StreamPosition revision,
        long maxCount = 9223372036854775807, bool resolveLinkTos = false, TimeSpan? deadline = null,
        UserCredentials? userCredentials = null, CancellationToken cancellationToken = new CancellationToken());

    Task<StreamSubscription> SubscribeToAllAsync(FromAll start, Func<StreamSubscription, ResolvedEvent, CancellationToken, Task> eventAppeared, bool resolveLinkTos = false,
        Action<StreamSubscription, SubscriptionDroppedReason, Exception?>? subscriptionDropped = null, SubscriptionFilterOptions? filterOptions = null,
        UserCredentials? userCredentials = null, CancellationToken cancellationToken = new CancellationToken());

    EventStoreClient.StreamSubscriptionResult SubscribeToAll(FromAll start, bool resolveLinkTos = false,
        SubscriptionFilterOptions? filterOptions = null, UserCredentials? userCredentials = null,
        CancellationToken cancellationToken = new CancellationToken());

    Task<StreamSubscription> SubscribeToStreamAsync(string streamName, FromStream start, Func<StreamSubscription, ResolvedEvent, CancellationToken, Task> eventAppeared, bool resolveLinkTos = false,
        Action<StreamSubscription, SubscriptionDroppedReason, Exception?>? subscriptionDropped = null, UserCredentials? userCredentials = null,
        CancellationToken cancellationToken = new CancellationToken());

    EventStoreClient.StreamSubscriptionResult SubscribeToStream(string streamName, FromStream start, bool resolveLinkTos = false,
        UserCredentials? userCredentials = null, CancellationToken cancellationToken = new CancellationToken());

    Task<DeleteResult> TombstoneAsync(string streamName, StreamRevision expectedRevision, TimeSpan? deadline = null,
        UserCredentials? userCredentials = null, CancellationToken cancellationToken = new CancellationToken());

    Task<DeleteResult> TombstoneAsync(string streamName, StreamState expectedState, TimeSpan? deadline = null,
        UserCredentials? userCredentials = null, CancellationToken cancellationToken = new CancellationToken());

    void Dispose();
}

class EventStoreClientRental : IEventStoreClientRental
{
    private readonly EventStoreClient _client;
    private readonly EventStoreClientPool _pool;

    public EventStoreClientRental(EventStoreClient client, EventStoreClientPool pool)
    {
        _client = client;
        _pool = pool;
    }

    public string ConnectionName => _client.ConnectionName;

    public Task<IWriteResult> AppendToStreamAsync(string streamName, StreamRevision expectedRevision, IEnumerable<EventData> eventData,
        Action<EventStoreClientOperationOptions>? configureOperationOptions = null, TimeSpan? deadline = null, UserCredentials? userCredentials = null,
        CancellationToken cancellationToken = new CancellationToken())
    {
        return _client.AppendToStreamAsync(streamName, expectedRevision, eventData, configureOperationOptions, deadline, userCredentials, cancellationToken);
    }

    public Task<IWriteResult> AppendToStreamAsync(string streamName, StreamState expectedState, IEnumerable<EventData> eventData,
        Action<EventStoreClientOperationOptions>? configureOperationOptions = null, TimeSpan? deadline = null, UserCredentials? userCredentials = null,
        CancellationToken cancellationToken = new CancellationToken())
    {
        return _client.AppendToStreamAsync(streamName, expectedState, eventData, configureOperationOptions, deadline, userCredentials, cancellationToken);
    }

    public Task<DeleteResult> DeleteAsync(string streamName, StreamRevision expectedRevision, TimeSpan? deadline = null,
        UserCredentials? userCredentials = null, CancellationToken cancellationToken = new CancellationToken())
    {
        return _client.DeleteAsync(streamName, expectedRevision, deadline, userCredentials, cancellationToken);
    }

    public Task<DeleteResult> DeleteAsync(string streamName, StreamState expectedState, TimeSpan? deadline = null,
        UserCredentials? userCredentials = null, CancellationToken cancellationToken = new CancellationToken())
    {
        return _client.DeleteAsync(streamName, expectedState, deadline, userCredentials, cancellationToken);
    }

    public Task<StreamMetadataResult> GetStreamMetadataAsync(string streamName, TimeSpan? deadline = null, UserCredentials? userCredentials = null,
        CancellationToken cancellationToken = new CancellationToken())
    {
        return _client.GetStreamMetadataAsync(streamName, deadline, userCredentials, cancellationToken);
    }

    public Task<IWriteResult> SetStreamMetadataAsync(string streamName, StreamState expectedState, StreamMetadata metadata,
        Action<EventStoreClientOperationOptions>? configureOperationOptions = null, TimeSpan? deadline = null, UserCredentials? userCredentials = null,
        CancellationToken cancellationToken = new CancellationToken())
    {
        return _client.SetStreamMetadataAsync(streamName, expectedState, metadata, configureOperationOptions, deadline, userCredentials, cancellationToken);
    }

    public Task<IWriteResult> SetStreamMetadataAsync(string streamName, StreamRevision expectedRevision, StreamMetadata metadata,
        Action<EventStoreClientOperationOptions>? configureOperationOptions = null, TimeSpan? deadline = null, UserCredentials? userCredentials = null,
        CancellationToken cancellationToken = new CancellationToken())
    {
        return _client.SetStreamMetadataAsync(streamName, expectedRevision, metadata, configureOperationOptions, deadline, userCredentials, cancellationToken);
    }

    public EventStoreClient.ReadAllStreamResult ReadAllAsync(Direction direction, Position position, long maxCount = 9223372036854775807,
        bool resolveLinkTos = false, TimeSpan? deadline = null, UserCredentials? userCredentials = null,
        CancellationToken cancellationToken = new CancellationToken())
    {
        return _client.ReadAllAsync(direction, position, maxCount, resolveLinkTos, deadline, userCredentials, cancellationToken);
    }

    public EventStoreClient.ReadAllStreamResult ReadAllAsync(Direction direction, Position position, IEventFilter eventFilter,
        long maxCount = 9223372036854775807, bool resolveLinkTos = false, TimeSpan? deadline = null,
        UserCredentials? userCredentials = null, CancellationToken cancellationToken = new CancellationToken())
    {
        return _client.ReadAllAsync(direction, position, eventFilter, maxCount, resolveLinkTos, deadline, userCredentials, cancellationToken);
    }

    public EventStoreClient.ReadStreamResult ReadStreamAsync(Direction direction, string streamName, StreamPosition revision,
        long maxCount = 9223372036854775807, bool resolveLinkTos = false, TimeSpan? deadline = null,
        UserCredentials? userCredentials = null, CancellationToken cancellationToken = new CancellationToken())
    {
        return _client.ReadStreamAsync(direction, streamName, revision, maxCount, resolveLinkTos, deadline, userCredentials, cancellationToken);
    }

    public Task<StreamSubscription> SubscribeToAllAsync(FromAll start, Func<StreamSubscription, ResolvedEvent, CancellationToken, Task> eventAppeared, bool resolveLinkTos = false,
        Action<StreamSubscription, SubscriptionDroppedReason, Exception?>? subscriptionDropped = null, SubscriptionFilterOptions? filterOptions = null,
        UserCredentials? userCredentials = null, CancellationToken cancellationToken = new CancellationToken())
    {
        return _client.SubscribeToAllAsync(start, eventAppeared, resolveLinkTos, subscriptionDropped, filterOptions, userCredentials, cancellationToken);
    }

    public EventStoreClient.StreamSubscriptionResult SubscribeToAll(FromAll start, bool resolveLinkTos = false,
        SubscriptionFilterOptions? filterOptions = null, UserCredentials? userCredentials = null,
        CancellationToken cancellationToken = new CancellationToken())
    {
        return _client.SubscribeToAll(start, resolveLinkTos, filterOptions, userCredentials, cancellationToken);
    }

    public Task<StreamSubscription> SubscribeToStreamAsync(string streamName, FromStream start, Func<StreamSubscription, ResolvedEvent, CancellationToken, Task> eventAppeared, bool resolveLinkTos = false,
        Action<StreamSubscription, SubscriptionDroppedReason, Exception?>? subscriptionDropped = null, UserCredentials? userCredentials = null,
        CancellationToken cancellationToken = new CancellationToken())
    {
        return _client.SubscribeToStreamAsync(streamName, start, eventAppeared, resolveLinkTos, subscriptionDropped, userCredentials, cancellationToken);
    }

    public EventStoreClient.StreamSubscriptionResult SubscribeToStream(string streamName, FromStream start, bool resolveLinkTos = false,
        UserCredentials? userCredentials = null, CancellationToken cancellationToken = new CancellationToken())
    {
        return _client.SubscribeToStream(streamName, start, resolveLinkTos, userCredentials, cancellationToken);
    }

    public Task<DeleteResult> TombstoneAsync(string streamName, StreamRevision expectedRevision, TimeSpan? deadline = null,
        UserCredentials? userCredentials = null, CancellationToken cancellationToken = new CancellationToken())
    {
        return _client.TombstoneAsync(streamName, expectedRevision, deadline, userCredentials, cancellationToken);
    }

    public Task<DeleteResult> TombstoneAsync(string streamName, StreamState expectedState, TimeSpan? deadline = null,
        UserCredentials? userCredentials = null, CancellationToken cancellationToken = new CancellationToken())
    {
        return _client.TombstoneAsync(streamName, expectedState, deadline, userCredentials, cancellationToken);
    }

    public void Dispose()
    {
        _pool.Return(_client);
    }

    
}