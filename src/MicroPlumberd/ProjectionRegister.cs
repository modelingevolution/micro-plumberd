using EventStore.Client;
using Grpc.Core;

namespace MicroPlumberd;

public interface IProjectionRegister
{
    Task<ProjectionDetails?> Get(string name);
}

static class Executor
{
    public static async Task<TResult> Retry<TException, TResult>(Func<Task<TResult>> action, int count = 3, int delay=100) where TException:Exception
    {
        TException last = null!;
        for(int i = 0; i < count; i++)
        try
        {
            return await action();
        }
        catch (TException e)
        {
            last = e;
            await Task.Delay(delay);
        }

        throw last!;
    }
}
class ProjectionRegister : IProjectionRegister
{
    private readonly AsyncLazy<Dictionary<string, ProjectionDetails>> _lazyLoader;
    private readonly EventStoreProjectionManagementClient _client;

    public ProjectionRegister(EventStoreProjectionManagementClient client)
    {
        _client = client;
        _lazyLoader = new AsyncLazy<Dictionary<string, ProjectionDetails>>(async () =>
        {
            return await Executor.Retry<RpcException,Dictionary<string, ProjectionDetails>>(async () =>
                await _client.ListContinuousAsync().ToDictionaryAsync(x => x.Name).AsTask(), delay:500);
        });
    }


    public async Task<ProjectionDetails?> Get(string name)
    {
        return (await _lazyLoader.Value).TryGetValue(name, out var v) ? v : null;
    }
}