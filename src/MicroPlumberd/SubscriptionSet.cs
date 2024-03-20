using EventStore.Client;

namespace MicroPlumberd;

class SubscriptionSet(Plumber plumber) : ISubscriptionSet
{
    private readonly Plumber plumber = plumber;
    private readonly Dictionary<string, Type> _register = new();
    private readonly Dictionary<string, List<IReadModel>> _dispatcher = new();
    
    public ISubscriptionSet With<TModel>(TModel model)
        where TModel : IReadModel, ITypeRegister
    {
        foreach(var i in TModel.TypeRegister)
        {
            _register.TryAdd(i.Key, i.Value);
            if (!_dispatcher.TryGetValue(i.Key, out var disp)) 
                _dispatcher.Add(i.Key, disp=new List<IReadModel>());
            if(!disp.Contains(model))
                disp.Add(model);
        }
        return this;
    }
    public async Task SubscribePersistentlyAsync(string outputStream, string? groupName = null)
    {
        groupName ??= outputStream;
        await plumber.ProjectionManagementClient.EnsureJoinProjection(outputStream, _register.Keys);
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

                var (ev, metadata) = plumber.ReadEventData(er, t);
               

                foreach (var i in models)
                    await i.Given(metadata, ev);
                await sub.Ack(e.Event.EventId);
            }
        }, state, TaskCreationOptions.LongRunning);
    }

   

    public async Task SubscribeAsync(string name, FromStream start)
    {
        await plumber.ProjectionManagementClient.EnsureJoinProjection(name, _register.Keys);
        EventStoreClient.StreamSubscriptionResult subscription = plumber.Pool.Rent().SubscribeToStream(name, start, true);
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

                var (ev, metadata) = plumber.ReadEventData(er, t);
                foreach (var i in models) 
                    await i.Given(metadata, ev);
            }
        }, state, TaskCreationOptions.LongRunning);
    }
}