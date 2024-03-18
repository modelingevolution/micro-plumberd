using EventStore.Client;
using Microsoft.Extensions.DependencyInjection;

namespace MicroPlumberd;

class SubscriptionRunner(Plumber plumber, EventStoreClient.StreamSubscriptionResult subscription) : ISubscriptionRunner
{
    public async Task<T> WithHandler<T>(T model)
        where T : IEventHandler, ITypeRegister
    {
        return await WithHandler<T>(model, T.TypeRegister.TryGetValue!);
    }

    public async Task<T> WithHandler<T>(T model, TypeEventConverter func)
        where T : IEventHandler
    {
        var state = new Tuple<EventStoreClient.StreamSubscriptionResult, T>(subscription, model);
        await Task.Factory.StartNew(async (x) =>
        {
            var (sub, model) = (Tuple<EventStoreClient.StreamSubscriptionResult, T>)x!;
            await foreach (var e in sub)
            {
                if (!func(e.Event.EventType, out var t)) continue;

                var (ev, metadata) = plumber.ReadEventData(e.Event, t);
                using var scope = new InvocationScope();
                plumber.Conventions.BuildInvocationContext(scope.Context, metadata);
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