﻿using EventStore.Client;

namespace MicroPlumberd;

class SubscriptionSet(Plumber plumber) : ISubscriptionSet
{
    private readonly Plumber plumber = plumber;
    private readonly Dictionary<string, Type> _register = new();
    private readonly Dictionary<string, List<IEventHandler>> _dispatcher = new();
    
    public ISubscriptionSet With<TModel>(TModel model)
        where TModel : IEventHandler, ITypeRegister
    {
        foreach(var i in plumber.TypeHandlerRegisters.GetEventNameMappingsFor<TModel>())
        {
            _register.TryAdd(i.Key, i.Value);
            if (!_dispatcher.TryGetValue(i.Key, out var disp)) 
                _dispatcher.Add(i.Key, disp=new List<IEventHandler>());
            if(!disp.Contains(model))
                disp.Add(model);
        }
        return this;
    }
    public async Task SubscribePersistentlyAsync(string outputStream, string? groupName = null)
    {
        groupName ??= outputStream;
        await plumber.ProjectionManagementClient.TryCreateJoinProjection(outputStream, _register.Keys);
        var subscription = plumber.PersistentSubscriptionClient.SubscribeToStream(outputStream, groupName);
        var state = Tuple.Create(this, subscription);

        await Task.Factory.StartNew(static async (x) =>
        {
            var (builder, sub) = (Tuple<SubscriptionSet, EventStorePersistentSubscriptionsClient.PersistentSubscriptionResult>)x!;
            var plumber = builder.plumber;
            await foreach (var e in sub)
            {
                var er = e.Event;
                if (!builder._dispatcher.TryGetValue(er.EventType, out var models)) continue;
                var t = builder._register[er.EventType];

                var (ev, metadata) = plumber.ReadEventData(er,e.Link, t);
               

                foreach (var i in models)
                    await i.Handle(metadata, ev);
                await sub.Ack(e.Event.EventId);
            }
        }, state, TaskCreationOptions.LongRunning);
    }

   

    public async Task SubscribeAsync(string name, FromStream start)
    {
        await plumber.ProjectionManagementClient.TryCreateJoinProjection(name, _register.Keys);
        
        EventStoreClient.StreamSubscriptionResult subscription = plumber.Client.SubscribeToStream(name, start, true);
        var state = Tuple.Create(this, subscription);
        
        await Task.Factory.StartNew(static async (x) =>
        {
            var (builder, sub) = (Tuple<SubscriptionSet, EventStoreClient.StreamSubscriptionResult>)x!;
            var plumber = builder.plumber;
            await foreach (var e in sub)
            {
                var er = e.Event;
                if (!builder._dispatcher.TryGetValue(er.EventType, out var models)) continue;
                var t = builder._register[er.EventType];

                var (ev, metadata) = plumber.ReadEventData(er,e.Link, t);
                foreach (var i in models) 
                    await i.Handle(metadata, ev);
            }
        }, state, TaskCreationOptions.LongRunning);
    }
}