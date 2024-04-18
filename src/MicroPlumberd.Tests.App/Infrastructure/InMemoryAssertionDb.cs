using System.Collections.Concurrent;
using LiteDB;

namespace MicroPlumberd.Tests.App.Infrastructure;

/// <summary>
/// This class is only for acting aa a mock.
/// </summary>
public class InMemoryAssertionDb
{
    public record Item(Metadata Metadata, object Event);

    public readonly SortedList<int, Item> Index = new();
    public readonly ConcurrentDictionary<Guid, List<Item>> IndexById = new();

    private int _i = -1;
    public void Add(Metadata m, object evt)
    {
        var i = new Item(m, evt);
        Index.Add(Interlocked.Increment(ref _i), i);
        IndexById.GetOrAdd(m.Id, x => new()).Add(i);
    }

    public async Task<T?> FindLast<T>(Guid id)
    {
        for (int i = 0; i < 100; i++)
            if (!IndexById.ContainsKey(id))
                await Task.Delay(100);
        return IndexById[id]
            .Where(x => x.Event is T)
            .Select(x => x.Event)
            .OfType<T>()
            .Reverse()
            .FirstOrDefault();
    }
}

public class LiteDbFactory
{
    private static int _id = 0;

    public static LiteDatabase Get()
    {
        string fileName = $"./test_{Interlocked.Increment(ref _id)}.db";
        if(File.Exists(fileName))
            File.Delete(fileName);
        return new LiteDatabase(fileName);
    }
}