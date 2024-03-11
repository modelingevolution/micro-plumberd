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
            var state = (Tuple<EventStoreClient.StreamSubscriptionResult, T>)x!;
            await foreach (var e in state.Item1)
            {
                if (!T.TypeRegister.TryGetValue(e.Event.EventType, out var t)) continue;

                var aggregateId = Guid.Parse(e.Event.EventStreamId.Substring(e.Event.EventStreamId.IndexOf('-') + 1));
                var ev = plumber.Serializer.Deserialize(e.Event.Data.Span, t)!;
                var m = plumber.Serializer.Parse(e.Event.Metadata.Span);
                await state.Item2.Given(new Metadata(aggregateId, m), ev);
            }
        }, state, TaskCreationOptions.LongRunning);
    }

    public async ValueTask DisposeAsync() => await subscription.DisposeAsync();
}