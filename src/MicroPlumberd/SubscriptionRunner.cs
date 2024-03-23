using EventStore.Client;
using Microsoft.Extensions.DependencyInjection;

namespace MicroPlumberd;

class SubscriptionRunner(Plumber plumber, EventStoreClient.StreamSubscriptionResult subscription) : ISubscriptionRunner
{
    public async Task<T> WithHandler<T>(T model)
        where T : IEventHandler, ITypeRegister
    {
        return await WithHandler<T>(model, plumber.TypeHandlerRegister.GetConverterFor<T>());
    }

    public async Task<T> WithHandler<T>(T model, TypeEventConverter func)
        where T : IEventHandler
    {
        return (T)await WithHandler((IEventHandler)model, func);
    }
    public async Task<IEventHandler> WithHandler(IEventHandler model, TypeEventConverter func)
    {
        var state = new Tuple<EventStoreClient.StreamSubscriptionResult, IEventHandler>(subscription, model);
        await Task.Factory.StartNew(async (x) =>
        {
            var (sub, model) = (Tuple<EventStoreClient.StreamSubscriptionResult, IEventHandler>)x!;
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

    public async Task<IEventHandler> WithHandler<T>(TypeEventConverter func) where T : IEventHandler
    {
        var handler = plumber.ServiceProvider.GetService<IEventHandler<T>>() ?? (IEventHandler)plumber.ServiceProvider.GetRequiredService<T>();
        return (IEventHandler)await WithHandler(handler, func);
    }
    public async Task<IEventHandler> WithHandler<T>() where T : IEventHandler, ITypeRegister => await WithHandler<T>(plumber.TypeHandlerRegister.GetConverterFor<T>());

    public async ValueTask DisposeAsync() => await subscription.DisposeAsync();
}

