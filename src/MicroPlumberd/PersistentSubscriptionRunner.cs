using System.Diagnostics;
using EventStore.Client;
using Microsoft.Extensions.DependencyInjection;

namespace MicroPlumberd;

class PersistentSubscriptionRunner(Plumber plumber, EventStorePersistentSubscriptionsClient.PersistentSubscriptionResult subscription) : ISubscriptionRunner
{
    public async Task<T> WithHandler<T>(T model)
        where T : IEventHandler, ITypeRegister
    {
        return await WithHandler<T>(model, T.TypeRegister.TryGetValue!);
    }

    public async Task<T> WithHandler<T>(T model, TypeEventConverter func)
        where T : IEventHandler
    {
        var state = new Tuple<EventStorePersistentSubscriptionsClient.PersistentSubscriptionResult, T>(subscription, model);
        await Task.Factory.StartNew(async (x) =>
        {
            var (sub, model) = (Tuple<EventStorePersistentSubscriptionsClient.PersistentSubscriptionResult, T>)x!;

            await foreach (var e in sub)
            {
                if (!func(e.Event.EventType, out var t)) continue;

                var (ev, metadata) = plumber.ReadEventData(e.Event, t);

                using var scope = new InvocationScope();
                plumber.Conventions.BuildInvocationContext(scope.Context, metadata);
                await model.Handle(metadata, ev);
                await sub.Ack(e);
            }

        }, state, TaskCreationOptions.LongRunning);
        return model;
    }

    public async Task<IEventHandler> WithHandler<T>(TypeEventConverter func) where T : IEventHandler
    {
        var handler = plumber.ServiceProvider.GetService<IEventHandler<T>>() ?? (IEventHandler)plumber.ServiceProvider.GetRequiredService<T>();
        return (T)await WithHandler(handler, func);
    }
    public async Task<IEventHandler> WithHandler<T>() where T : IEventHandler, ITypeRegister => await WithHandler<T>(T.TypeRegister.TryGetValue);

    public async ValueTask DisposeAsync() => await subscription.DisposeAsync();
}