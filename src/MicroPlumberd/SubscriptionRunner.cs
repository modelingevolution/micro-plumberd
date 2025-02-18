using EventStore.Client;
using Microsoft.Extensions.DependencyInjection;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace MicroPlumberd;


record SubscriptionRunnerState : IDisposable
{
    private IDisposable? _subscription;

    public SubscriptionRunnerState(FromStream initialPosition, EventStoreClient client, string streamName, UserCredentials? userCredentials, CancellationToken cancellationToken)
    {
        _initialPosition = initialPosition;
        _client = client;
        this.StreamName = streamName;
        this.UserCredentials = userCredentials;
        this.CancellationToken = cancellationToken;
        Position = initialPosition;
    }

    public EventStoreClient.StreamSubscriptionResult Subscribe()
    {
        var result = _client.SubscribeToStream(StreamName, Position, true, UserCredentials, CancellationToken);
        _subscription = result;
        return result;
    }
    public FromStream Position { get; set; }
    public IEventHandler Handler { get; set; }
    private readonly FromStream _initialPosition;
    private readonly EventStoreClient _client;
    public string StreamName { get; init; }
    public UserCredentials? UserCredentials { get; init; }
    public CancellationToken CancellationToken { get; init; }

    public void Dispose()
    {
        _subscription?.Dispose();
    }

   
};
class SubscriptionSeeker(Plumber plumber, string streamName, FromRelativeStreamPosition start,
    UserCredentials? userCredentials = null, CancellationToken cancellationToken = default) : ISubscriptionRunner
{
    private SubscriptionRunner? _runner;

    
    private async Task<SubscriptionRunnerState> Subscribe()
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

        var records = plumber.Client.ReadStreamAsync(start.Direction, streamName, sp, 1, 
            cancellationToken:cancellationToken);
        StreamPosition dstPosition;
        if (await records.ReadState == ReadState.StreamNotFound)
            return new (subscriptionStart,plumber.Client,streamName, userCredentials,  cancellationToken);

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

        return new(subscriptionStart, plumber.Client, streamName, userCredentials, cancellationToken);
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
    public async Task<IEventHandler> WithHandler<T>(ITypeHandlerRegisters register) where T : IEventHandler, ITypeRegister => await WithHandler<T>(register.GetEventNameConverterFor<T>());
    public async Task<IEventHandler> WithHandler<T>(T model, ITypeHandlerRegisters register) where T : IEventHandler, ITypeRegister => await WithHandler<T>(model, register.GetEventNameConverterFor<T>());
    public async Task<IEventHandler> WithSnapshotHandler<T>() where T : IEventHandler, ITypeRegister => await WithHandler<T>(new TypeHandlerRegisters((ownerType, eventType) => plumber.Conventions.SnapshotEventNameConvention(eventType)).GetEventNameConverterFor<T>());
    public async Task<IEventHandler> WithSnapshotHandler<T>(T model) where T : IEventHandler, ITypeRegister => await WithHandler<T>(model, new TypeHandlerRegisters((ownerType, eventType) => plumber.Conventions.SnapshotEventNameConvention(eventType)).GetEventNameConverterFor<T>());

    public async ValueTask DisposeAsync() => await _runner.DisposeAsync();
}
class SubscriptionRunner(Plumber plumber, SubscriptionRunnerState subscription) : ISubscriptionRunner
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
        subscription.Handler = model;
        await Task.Factory.StartNew(async (_) =>
        {
            var l = plumber.Config.ServiceProvider.GetService<ILogger<SubscriptionRunner>>();
            while (!subscription.CancellationToken.IsCancellationRequested)
            {
                try
                {
                    await using var sub = subscription.Subscribe();
                    await foreach (var m in sub.Messages)
                    {
                        switch (m)
                        {
                            case StreamMessage.Event(var e):
                                await OnEvent(func, e, model);
                                subscription.Position = FromStream.After(e.OriginalEventNumber);
                                break;
                            case StreamMessage.CaughtUp:
                                if (model is ICaughtUpHandler cuh) await cuh.CaughtUp();
                                break;
                            default: break;

                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    l?.LogDebug($"Subscription '{subscription.StreamName}' was canceled.");
                    return;
                }
                catch (ObjectDisposedException)
                {
                    l?.LogDebug($"Subscription '{subscription.StreamName}' was canceled.");
                    return;
                }
                catch (Exception ex)
                {
                    l?.LogWarning($"Subscription '{subscription.StreamName}' was dropped. Will retry in 5sec.");
                    await Task.Delay(5000, subscription.CancellationToken);
                }
            }
        }, subscription, TaskCreationOptions.LongRunning);
        return model;
    }

    private async Task OnEvent(TypeEventConverter func, ResolvedEvent e, IEventHandler model)
    {
        while(!subscription.CancellationToken.IsCancellationRequested)
        try
        {
            if (!func(e.Event.EventType, out var t)) return;

            var (ev, metadata) = plumber.ReadEventData(e.Event, e.Link,t);
            using var scope = new InvocationScope();
            plumber.Conventions.BuildInvocationContext(scope.Context, metadata);
            await model.Handle(metadata, ev);
            return;
        }
        catch (Exception ex)
        {
            var l = plumber.Config.ServiceProvider.GetService<ILogger<SubscriptionRunner>>();
            
            l?.LogError(ex, $"Subscription '{subscription.StreamName}' encountered unhandled exception. Most likely because of Given/Handle methods throwing exceptions. Retry in 30sec.");
            var decision = await
                plumber.Config.HandleError(ex, subscription.StreamName, subscription.CancellationToken);
            switch (decision)
            {
                case ErrorHandleDecision.Retry:
                    continue;
                case ErrorHandleDecision.Cancel:
                    throw new OperationCanceledException("Operation canceled by user.");
                case ErrorHandleDecision.Ignore:
                    return;
            }

            
        }
    }

    public async Task<IEventHandler> WithHandler<T>(TypeEventConverter func) where T : IEventHandler
    {
        var handler = plumber.ServiceProvider.GetService<IEventHandler<T>>() ?? (IEventHandler)plumber.ServiceProvider.GetRequiredService<T>();
        return (IEventHandler)await WithHandler(handler, func);
    }
    public async Task<IEventHandler> WithHandler<T>() where T : IEventHandler, ITypeRegister => await WithHandler<T>(plumber.TypeHandlerRegisters.GetEventNameConverterFor<T>());

    public async ValueTask DisposeAsync() => subscription.Dispose();

    public async Task<IEventHandler> WithHandler<T>(ITypeHandlerRegisters register) where T : IEventHandler, ITypeRegister => await WithHandler<T>(register.GetEventNameConverterFor<T>());
    public async Task<IEventHandler> WithHandler<T>(T model, ITypeHandlerRegisters register) where T : IEventHandler, ITypeRegister => await WithHandler<T>(model, register.GetEventNameConverterFor<T>());
    public async Task<IEventHandler> WithSnapshotHandler<T>() where T : IEventHandler, ITypeRegister => await WithHandler<T>(new TypeHandlerRegisters((ownerType, eventType) => plumber.Conventions.SnapshotEventNameConvention(eventType)).GetEventNameConverterFor<T>());
    public async Task<IEventHandler> WithSnapshotHandler<T>(T model) where T : IEventHandler, ITypeRegister => await WithHandler<T>(model, new TypeHandlerRegisters((ownerType, eventType) => plumber.Conventions.SnapshotEventNameConvention(eventType)).GetEventNameConverterFor<T>());

}

