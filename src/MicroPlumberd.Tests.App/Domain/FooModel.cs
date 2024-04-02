namespace MicroPlumberd.Tests.App.Domain;

[OutputStream("FooModel_v1")]
[EventHandler]
public partial class FooModel
{
    public readonly Dictionary<Guid, string> Index = new();
    public readonly List<Metadata> Metadatas = new();
    public readonly List<object> Events = new();
    public string? FindById(Guid id) => Index.TryGetValue(id, out var v) ? v : null;
    private async Task Given(Metadata m, FooCreated ev)
    {
        Index.Add(m.Id, ev.Name!);
        Metadatas.Add(m);
        Events.Add(ev);
        await Task.Delay(0);
    }
    private async Task Given(Metadata m, FooUpdated ev)
    {
        Index[m.Id] = ev.Name!;
        Metadatas.Add(m);
        Events.Add(ev);
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
