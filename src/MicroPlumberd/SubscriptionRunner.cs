using EventStore.Client;
using Microsoft.Extensions.DependencyInjection;
using System.Threading;

namespace MicroPlumberd;

class DelayedSubscriptionRunner(Plumber plumber, string streamName, FromRelativeStreamPosition start,
    UserCredentials? userCredentials = null, CancellationToken cancellationToken = new()) : ISubscriptionRunner
{
    private SubscriptionRunner? _runner;

    private async Task<EventStoreClient.StreamSubscriptionResult> Subscribe()
    {
        StreamPosition sp = StreamPosition.Start;
        FromStream subscriptionStart = FromStream.Start;

        if (start.StartPosition == FromStream.End)
        {
            sp = StreamPosition.End;
            subscriptionStart = FromStream.End;
        }
        else if (start.StartPosition != FromStream.Start)
            sp = start.StartPosition.ToUInt64();

        var records = plumber.Client.ReadStreamAsync(start.Direction, streamName, sp, 1);
        StreamPosition dstPosition;
        if (await records.ReadState == ReadState.StreamNotFound)
            return plumber.Client.SubscribeToStream(streamName, subscriptionStart, true, userCredentials, cancellationToken);

        var record = await records.FirstAsync();
        if (start.Direction == Direction.Forwards)
        {
            dstPosition = record.Event.EventNumber + start.Count;
            subscriptionStart = FromStream.After(dstPosition);
        }
        else
        {
            ulong en = record.OriginalEventNumber.ToUInt64();
            if (en >= start.Count)
            {
                dstPosition = record.Event.EventNumber - start.Count;
                subscriptionStart = FromStream.After(dstPosition);
            }
            else subscriptionStart = FromStream.Start;
        }

        return plumber.Client.SubscribeToStream(streamName, subscriptionStart, true, userCredentials,
            cancellationToken);
    }
    public async Task<T> WithHandler<T>(T model)
        where T : IEventHandler, ITypeRegister
    {
        return await WithHandler<T>(model, plumber.TypeHandlerRegisters.GetEventNameConverterFor<T>());
    }

    public async Task<T> WithHandler<T>(T model, TypeEventConverter func)
        where T : IEventHandler
    {
        return (T)await WithHandler((IEventHandler)model, func);
    }
    public async Task<IEventHandler> WithHandler(IEventHandler model, TypeEventConverter func)
    {
        _runner = new SubscriptionRunner(plumber, await Subscribe());
        await _runner.WithHandler(model, func);
        
        return model;
    }

    public async Task<IEventHandler> WithHandler<T>(TypeEventConverter func) where T : IEventHandler
    {
        var handler = plumber.ServiceProvider.GetService<IEventHandler<T>>() ?? (IEventHandler)plumber.ServiceProvider.GetRequiredService<T>();
        return (IEventHandler)await WithHandler(handler, func);
    }
    public async Task<IEventHandler> WithHandler<T>() where T : IEventHandler, ITypeRegister => await WithHandler<T>(plumber.TypeHandlerRegisters.GetEventNameConverterFor<T>());

    public async ValueTask DisposeAsync() => await _runner.DisposeAsync();
}
class SubscriptionRunner(Plumber plumber, EventStoreClient.StreamSubscriptionResult subscription) : ISubscriptionRunner
{
    public async Task<T> WithHandler<T>(T model)
        where T : IEventHandler, ITypeRegister
    {
        return await WithHandler<T>(model, plumber.TypeHandlerRegisters.GetEventNameConverterFor<T>());
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
    public async Task<IEventHandler> WithHandler<T>() where T : IEventHandler, ITypeRegister => await WithHandler<T>(plumber.TypeHandlerRegisters.GetEventNameConverterFor<T>());

    public async ValueTask DisposeAsync() => await subscription.DisposeAsync();
}

