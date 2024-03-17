using EventStore.Client;

namespace MicroPlumberd;

public static class EventStoreProjectionManagementClientExtensions
{
    public static async Task EnsureJoinProjection(this EventStoreProjectionManagementClient client,
        string outputStream, IEnumerable<string> eventTypes)
    {
        var query = CreateQuery(outputStream, eventTypes);

        if (await client.ListContinuousAsync().AnyAsync(x => x.Name == outputStream))
            await Update(client, outputStream, query);
        else
            await client.CreateContinuousAsync(outputStream, query, true);
    }

    private static async Task Update(EventStoreProjectionManagementClient client, string outputStream, string query)
    {
        var state = await client.GetStatusAsync(outputStream);
        if (state!.Status != "Stopped")
            await client.DisableAsync(outputStream);
        await client.UpdateAsync(outputStream, query, true);
        await client.EnableAsync(outputStream);
    }

    public static async Task EnsureJoinProjection(this EventStoreProjectionManagementClient client,
        string outputStream, IProjectionRegister register, IEnumerable<string> eventTypes)
    {
        var query = CreateQuery(outputStream, eventTypes);

        if ((await register.Get(outputStream)) != null)
            await Update(client, outputStream, query);
        else
        {
            await client.CreateContinuousAsync(outputStream, query, false);
            await client.DisableAsync(outputStream);
            await client.UpdateAsync(outputStream, query, true);
            await client.EnableAsync(outputStream);
        }
    }

    private static string CreateQuery(string outputStream, IEnumerable<string> eventTypes)
    {
        string fromStreamsArg = string.Join(',', eventTypes.Select(x => $"'$et-{x}'"));
        string query = $"fromStreams([{fromStreamsArg}]).when( {{ " +
                       $"\n    $any : function(s,e) {{ linkTo('{outputStream}', e) }}" +
                       $"\n}});";
        return query;
    }
}