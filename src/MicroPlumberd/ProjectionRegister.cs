using EventStore.Client;

namespace MicroPlumberd;

public interface IProjectionRegister
{
    Task<ProjectionDetails?> Get(string name);
}

class ProjectionRegister : IProjectionRegister
{
    private readonly AsyncLazy<Dictionary<string, ProjectionDetails>> _lazyLoader;
    private readonly EventStoreProjectionManagementClient _client;

    public ProjectionRegister(EventStoreProjectionManagementClient client)
    {
        _client = client;
        _lazyLoader = new AsyncLazy<Dictionary<string, ProjectionDetails>>(async () => await _client.ListContinuousAsync().ToDictionaryAsync(x=>x.Name).AsTask());
    }


    public async Task<ProjectionDetails?> Get(string name)
    {
        return (await _lazyLoader.Value).TryGetValue(name, out var v) ? v : null;
    }
}