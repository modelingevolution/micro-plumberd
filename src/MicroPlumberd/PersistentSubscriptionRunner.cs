using System.Diagnostics;
using EventStore.Client;
using Microsoft.Extensions.DependencyInjection;

namespace MicroPlumberd;

class PersistentSubscriptionRunner(Plumber plumber, EventStorePersistentSubscriptionsClient.PersistentSubscriptionResult subscription) : ISubscriptionRunner
{
    public async Task<T> WithHandler<T>(T model) where T : IEventHandler, ITypeRegister
    {
        var state = new Tuple<EventStorePersistentSubscriptionsClient.PersistentSubscriptionResult, T>(subscription, model);
        await Task.Factory.StartNew(async (x) =>
        {
            var (sub, model) = (Tuple<EventStorePersistentSubscriptionsClient.PersistentSubscriptionResult, T>)x!;

            await foreach (var e in sub)
            {
                if (!T.TypeRegister.TryGetValue(e.Event.EventType, out var t)) continue;

                var (ev, metadata) = plumber.ReadEventData(e.Event, t);
                await model.Handle(metadata, ev);
                await sub.Ack(e);
            }

        }, state, TaskCreationOptions.LongRunning);
        return model;
    }

    public async Task<T> WithHandler<T>() where T : IEventHandler, ITypeRegister
    {
        return await WithHandler(plumber.ServiceProvider.GetRequiredService<T>());
    }

    public async ValueTask DisposeAsync() => await subscription.DisposeAsync();
}