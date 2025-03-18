using System.Diagnostics;
using EventStore.Client;
using Microsoft.Extensions.DependencyInjection;

namespace MicroPlumberd;

class PersistentSubscriptionRunner(PlumberEngine plumber, EventStorePersistentSubscriptionsClient.PersistentSubscriptionResult subscription) : ISubscriptionRunner
{
    public async Task<T> WithHandler<T>(T model)
        where T : IEventHandler, ITypeRegister
    {
        return await WithHandler<T>(model, plumber.TypeHandlerRegisters.GetEventNameConverterFor<T>());
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

                OperationContext.ClearContext();
                var context = OperationContext.Create(Flow.EventHandler);
                using var scope = context.CreateScope();

               context.SetStreamName(subscription.StreamName);

                var (ev, metadata) = plumber.ReadEventData(context,e.Event,e.Link, t);

                //using var scope = new InvocationScope();
                //plumber.Conventions.BuildInvocationContext(scope.Context, metadata);
                
               context.SetCausationId(metadata.CausationId());
               context.SetCorrelationId(metadata.CorrelationId());
               context.SetUserId(metadata.UserId());

                await model.Handle(metadata, ev);
                await sub.Ack(e);
            }

        }, state, TaskCreationOptions.LongRunning);
        return model;
    }

    public async Task<IEventHandler> WithHandler<T>(TypeEventConverter func) where T : IEventHandler
    {
        var handler = plumber.ServiceProvider.GetService<IEventHandler<T>>() ?? (IEventHandler)plumber.ServiceProvider.GetRequiredService<T>();
        return (IEventHandler)await WithHandler(handler, func);
    }
    public async Task<IEventHandler> WithHandler<T>() where T : IEventHandler, ITypeRegister => await WithHandler<T>(plumber.TypeHandlerRegisters.GetEventNameConverterFor<T>());

    public async ValueTask DisposeAsync() => await subscription.DisposeAsync();

    public async Task<IEventHandler> WithHandler<T>(ITypeHandlerRegisters register) where T : IEventHandler, ITypeRegister => await WithHandler<T>(register.GetEventNameConverterFor<T>());
    public async Task<IEventHandler> WithHandler<T>(T model, ITypeHandlerRegisters register) where T : IEventHandler, ITypeRegister => await WithHandler<T>(model,register.GetEventNameConverterFor<T>());
    public async Task<IEventHandler> WithSnapshotHandler<T>() where T : IEventHandler, ITypeRegister => await WithHandler<T>(new TypeHandlerRegisters((ownerType, eventType) => plumber.Conventions.SnapshotEventNameConvention(eventType)).GetEventNameConverterFor<T>());
    public async Task<IEventHandler> WithSnapshotHandler<T>(T model) where T : IEventHandler, ITypeRegister => await WithHandler<T>(model, new TypeHandlerRegisters((ownerType, eventType) => plumber.Conventions.SnapshotEventNameConvention(eventType)).GetEventNameConverterFor<T>());

}