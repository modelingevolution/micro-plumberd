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
            var (sub, model) = (Tuple<EventStorePersistentSubscriptionsClient.PersistentSubscriptionResult, T>)x!;
            await foreach (var e in sub)
            {
                if (!T.TypeRegister.TryGetValue(e.Event.EventType, out var t)) continue;

                var (ev, metadata) = plumber.ReadEventData(e.Event, t);
                await model.Given(metadata, ev);
                await sub.Ack(e.Event.EventId);
            }
        }, state, TaskCreationOptions.LongRunning);
    }

    public async ValueTask DisposeAsync() => await subscription.DisposeAsync();
}