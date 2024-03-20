using EventStore.Client;

namespace MicroPlumberd;

public interface IProjectionRegister
{
    Task<ProjectionDetails?> Get(string name);
}
class ProjectionRegister(EventStoreProjectionManagementClient client) : IProjectionRegister
{
    private readonly Dictionary<string, ProjectionDetails> _index = new();
    private bool _initialized = false;
    private object _sync = new object();
    private readonly ManualResetEventSlim _ready = new ManualResetEventSlim(false);
    public async Task<ProjectionDetails?> Get(string name)
    {
        if (!_initialized )
        {
            bool load = false;
            lock (_sync)
            {
                if (!_initialized)
                {
                    load = true;
                    _initialized = true;
                }
            }

            if (load)
            {
                for (int r = 0; r < 3; r++)
                {
                    try
                    {
                        var projections = await client.ListContinuousAsync().ToArrayAsync();
                        foreach (var i in projections)
                        {
                            _index.Add(i.Name, i);
                        }

                        break;
                    }
                    catch
                    {
                        await Task.Delay(1000);
                    }
                }

                _ready.Set();
            }
            else
                _ready.Wait();
        }

        return _index.TryGetValue(name, out var v) ? v : null;
    }
}