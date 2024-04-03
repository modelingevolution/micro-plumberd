using System.Collections.Concurrent;

namespace MicroPlumberd.Tests.App.Domain;

public class InMemoryModelStore
{
    public record Item(Metadata Metadata, object Event);

    public readonly SortedList<int, Item> Index = new();
    public readonly ConcurrentDictionary<Guid, List<Item>> IndexById = new();

    private int _i = -1;
    public void Given(Metadata m, object evt)
    {
        var i = new Item(m,evt);
        Index.Add(Interlocked.Increment(ref _i),i);
        IndexById.GetOrAdd(m.Id, x => new()).Add(i);
    }

    public async Task<T?> FindLast<T>(Guid id)
    {
        for(int i = 0; i < 100; i++)
            if (!IndexById.ContainsKey(id))
                await Task.Delay(100);
        return IndexById[id]
            .Where(x=>x.Event is T)
            .Select(x=>x.Event)
            .OfType<T>()
            .Reverse()
            .FirstOrDefault();
    }
}

[OutputStream("FooModel_v1")]
[EventHandler]
public partial class FooModel(InMemoryModelStore assertionModelStore)
{
    public InMemoryModelStore ModelStore => assertionModelStore;
    public async Task<string?> FindById(Guid id) => (await assertionModelStore.FindLast<FooCreated>(id))?.Name;
    private async Task Given(Metadata m, FooCreated ev)
    {
        assertionModelStore.Given(m,ev);
         await Task.Delay(0);
    }
    private async Task Given(Metadata m, FooRefined ev)
    {
        assertionModelStore.Given(m, ev);
        await Task.Delay(0);
    }
}


[EventHandler]
public partial class MasterModel(DimentionLookupModel lookup)
{
    public record MasterRecord(Guid Id, string Name, string DependencyName, Guid DependencyId);
    public readonly Dictionary<Guid, MasterRecord> Index = new();
    public readonly List<Metadata> Metadatas = new();
    public readonly List<object> Events = new();
   
    private async Task Given(Metadata m, MasterRecordCreated ev)
    {
        Index[m.Id] = new MasterRecord(m.Id, ev.Name, lookup.Index[ev.DependencyId], ev.DependencyId);
        Metadatas.Add(m);
        Events.Add(ev);
        await Task.Delay(0);
    }
}

public class DimentionCreated
{
    public string Name { get; init; }
}
public record MasterRecordCreated
{
    public string Name { get; init; }
    public Guid DependencyId { get; init; }
}

[EventHandler]
public partial class DimentionLookupModel
{
    public readonly Dictionary<Guid, string> Index = new();
    
    private async Task Given(Metadata m, DimentionCreated ev)
    {
        Index.Add(m.Id, ev.Name!);
        await Task.Delay(0);
    }
   
}
