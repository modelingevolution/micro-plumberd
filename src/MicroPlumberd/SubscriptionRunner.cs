using EventStore.Client;
using Microsoft.Extensions.DependencyInjection;

namespace MicroPlumberd;

class SubscriptionRunner(Plumber plumber, EventStoreClient.StreamSubscriptionResult subscription) : ISubscriptionRunner
{
    public async Task<T> WithHandler<T>(T model)
        where T : IEventHandler, ITypeRegister
    {
        var state = new Tuple<EventStoreClient.StreamSubscriptionResult, T>(subscription, model);
        await Task.Factory.StartNew(async (x) =>
        {
            var (sub, model) = (Tuple<EventStoreClient.StreamSubscriptionResult, T>)x!;
            await foreach (var e in sub)
            {
                if (!T.TypeRegister.TryGetValue(e.Event.EventType, out var t)) continue;

                var (ev, metadata) = plumber.ReadEventData(e.Event, t);
                await model.Handle(metadata, ev);
            }
        }, state, TaskCreationOptions.LongRunning);
        return model;
    }

    public async Task<T> WithHandler<T>() where T : IEventHandler, ITypeRegister
    {
        var handler = plumber.ServiceProvider.GetRequiredService<T>();
        return await WithHandler(handler);
    }

    public async ValueTask DisposeAsync() => await subscription.DisposeAsync();
}