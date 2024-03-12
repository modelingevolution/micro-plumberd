namespace MicroPlumberd.Tests.AppSrc;

[EventHandler]
public partial class FooModel
{
    public readonly Dictionary<Guid, string> Index = new();
    public readonly List<Metadata> Metadatas = new();
    public readonly List<object> Events = new();
    private async Task Given(Metadata m, FooCreated ev)
    {
        Index.Add(m.Id, ev.Name);
        Metadatas.Add(m);
        Events.Add(ev);
    }
    private async Task Given(Metadata m, FooUpdated ev)
    {
        Index[m.Id] = ev.Name;
        Metadatas.Add(m);
        Events.Add(ev);
    }
}