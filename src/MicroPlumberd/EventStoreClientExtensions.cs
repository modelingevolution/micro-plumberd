using System.Text.Json;
using EventStore.Client;

namespace MicroPlumberd;
using PSR = EventStorePersistentSubscriptionsClient.PersistentSubscriptionResult;
public static class EventStoreClientExtensions
{

    public static async Task<T> Get<T>(this EventStoreClient client, Guid id)
        where T : IAggregate<T>
    {
        var items = client.ReadStreamAsync(Direction.Forwards, $"{typeof(T).Name}-{id}", StreamPosition.Start);
        var registry = T.TypeRegister;
        var events = items.Select(ev => Serializer.Instance.Deserialize(ev.Event.Data.Span, registry[ev.Event.EventType])!);

        var aggregate = T.New(id);
        await aggregate.Rehydrate(events);
        return aggregate;
    }

    public static async Task SaveChanges<T>(this EventStoreClient client, T aggregate, Func<T, object, object> m = null)
        where T : IAggregate<T>
    {
        string streamId = $"{typeof(T).Name}-{aggregate.Id}";
        var evData = aggregate.PendingEvents.Select(x =>
        {
            var metadata = m?.Invoke(aggregate, x);
            return new EventData(Uuid.NewUuid(), x.GetType().Name, JsonSerializer.SerializeToUtf8Bytes(x),
                Serializer.Instance.SerializeToUtf8Bytes(metadata));
        });
        await client.AppendToStreamAsync(streamId, StreamRevision.FromInt64(aggregate.Age), evData);
    }

    public static async Task SaveNew<T>(this EventStoreClient client, T aggregate, Func<T,object,object> m = null)
        where T : IAggregate<T>
    {
        string streamId = $"{typeof(T).Name}-{aggregate.Id}";
        var evData = aggregate.PendingEvents.Select(x =>
        {
            var metadata = m?.Invoke(aggregate, x);
            return new EventData(Uuid.NewUuid(), x.GetType().Name, Serializer.Instance.SerializeToUtf8Bytes(x), 
                Serializer.Instance.SerializeToUtf8Bytes(metadata));
        });
        await client.AppendToStreamAsync(streamId, StreamState.NoStream, evData);
    }
    public static async Task WithModel<T>(this EventStoreClient.StreamSubscriptionResult events, T model)
        where T : IReadModel<T>

    {
        var state = new Tuple<EventStoreClient.StreamSubscriptionResult, T>(events, model);
        await Task.Factory.StartNew(static async (x) =>
        {
            var state = (Tuple<EventStoreClient.StreamSubscriptionResult, T>)x!;
            await foreach (var e in state.Item1)
            {
                if (!T.TypeRegister.TryGetValue(e.Event.EventType, out var t)) continue;

                var aggregateId = Guid.Parse(e.Event.EventStreamId.Substring(e.Event.EventStreamId.IndexOf('-') + 1));
                var ev = Serializer.Instance.Deserialize(e.Event.Data.Span, t)!;
                var m = Serializer.Instance.Parse(e.Event.Metadata.Span);
                await state.Item2.Given(new Metadata(aggregateId, m), ev);
            }
        }, state, TaskCreationOptions.LongRunning);
    }

    public static async Task WithModel<T>(this PSR sub, T model)
        where T : IReadModel<T>
    {
        var state = new Tuple<PSR, T>(sub, model);
        await Task.Factory.StartNew(static async (x) =>
        {
            var state = (Tuple<PSR, T>)x!;
            await foreach (var e in state.Item1)
            {
                if (!T.TypeRegister.TryGetValue(e.Event.EventType, out var t)) continue;

                var aggregateId = Guid.Parse(e.Event.EventStreamId.Substring(e.Event.EventStreamId.IndexOf('-') + 1));
                var ev = Serializer.Instance.Deserialize(e.Event.Data.Span, t)!;
                var m = Serializer.Instance.Parse(e.Event.Metadata.Span);
                await state.Item2.Given(new Metadata(aggregateId, m), ev);
                await state.Item1.Ack(e.Event.EventId);
            }
        }, state, TaskCreationOptions.LongRunning);
    }
}

