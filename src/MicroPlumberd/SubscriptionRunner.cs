using EventStore.Client;

namespace MicroPlumberd;

class SubscriptionRunner(Plumber plumber, EventStoreClient.StreamSubscriptionResult subscription) : ISubscriptionRunner
{
    public async Task WithModel<T>(T model)
        where T : IReadModel, ITypeRegister
    {
        var state = new Tuple<EventStoreClient.StreamSubscriptionResult, T>(subscription, model);
        await Task.Factory.StartNew(async (x) =>
        {
            var (sub, model) = (Tuple<EventStoreClient.StreamSubscriptionResult, T>)x!;
            await foreach (var e in sub)
            {
                if (!T.TypeRegister.TryGetValue(e.Event.EventType, out var t)) continue;

                var (ev, metadata) = plumber.ReadEventData(e.Event, t);
                await model.Given(metadata, ev);
            }
        }, state, TaskCreationOptions.LongRunning);
    }

    public async ValueTask DisposeAsync() => await subscription.DisposeAsync();
}