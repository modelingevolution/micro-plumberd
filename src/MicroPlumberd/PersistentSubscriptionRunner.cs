using EventStore.Client;

namespace MicroPlumberd;

class PersistentSubscriptionRunner(Plumber plumber, EventStorePersistentSubscriptionsClient.PersistentSubscriptionResult subscription) : ISubscriptionRunner
{
    public async Task WithModel<T>(T model)
        where T : IReadModel, ITypeRegister
    {
        var state = new Tuple<EventStorePersistentSubscriptionsClient.PersistentSubscriptionResult, T>(subscription, model);
        await Task.Factory.StartNew(async (x) =>
        {
            var state = (Tuple<EventStorePersistentSubscriptionsClient.PersistentSubscriptionResult, T>)x!;
            await foreach (var e in state.Item1)
            {
                if (!T.TypeRegister.TryGetValue(e.Event.EventType, out var t)) continue;

                var aggregateId = Guid.Parse(e.Event.EventStreamId.Substring(e.Event.EventStreamId.IndexOf('-') + 1));
                var ev = plumber.Serializer.Deserialize(e.Event.Data.Span, t)!;
                var m = plumber.Serializer.Parse(e.Event.Metadata.Span);
                await state.Item2.Given(new Metadata(aggregateId, m), ev);
                await state.Item1.Ack(e.Event.EventId);
            }
        }, state, TaskCreationOptions.LongRunning);
    }

    public async ValueTask DisposeAsync() => await subscription.DisposeAsync();
}