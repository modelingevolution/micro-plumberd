using System.Collections.Concurrent;
using EventStore.Client;

namespace MicroPlumberd;

public interface IEventStoreClientPool
{
    IEventStoreClientRental Rent();
}

class EventStoreClientPool(EventStoreClientSettings settings) : IEventStoreClientPool
{
    private ConcurrentBag<EventStoreClient> _pool = new();

    public IEventStoreClientRental Rent()
    {
        if (!_pool.TryTake(out var c))
        {
            EventStoreClient client = new EventStoreClient(settings);
            EventStoreClientRental r = new EventStoreClientRental(client, this);
            return r;
        }

        return new EventStoreClientRental(c, this);
    }

    public void Return(EventStoreClient client)
    {
        _pool.Add(client);
    }
}