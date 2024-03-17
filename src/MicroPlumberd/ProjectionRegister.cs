using EventStore.Client;

namespace MicroPlumberd;

public interface IProjectionRegister
{
    Task<ProjectionDetails?> Get(string name);
}
class ProjectionRegister(EventStoreProjectionManagementClient client) : IProjectionRegister
{
    private readonly Dictionary<string, ProjectionDetails> _index = new();
    public async Task<ProjectionDetails?> Get(string name)
    {
        if (_index.Count == 0)
        {
            var projections = await client.ListContinuousAsync().ToArrayAsync();
            foreach (var i in projections)
            {
                _index.Add(i.Name, i);
            }
        }

        return _index.TryGetValue(name, out var v) ? v : null;
    }
}