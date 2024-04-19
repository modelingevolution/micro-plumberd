using EventStore.Client;
using Microsoft.Win32;

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

    private static async Task Update(EventStoreProjectionManagementClient client, string outputStream, string query, CancellationToken token = default)
    {
        var state = await client.GetStatusAsync(outputStream, cancellationToken: token);
        if (state!.Status != "Stopped")
            await client.DisableAsync(outputStream, cancellationToken: token);
        await client.UpdateAsync(outputStream, query, true, cancellationToken: token);
        await client.EnableAsync(outputStream, cancellationToken: token);
    }

    public static async Task EnsureLookupProjection(this EventStoreProjectionManagementClient client, IProjectionRegister register, string category, string eventProperty, string outputStreamCategory, CancellationToken token = default)
    {
        string query =
            $"fromStreams(['$ce-{category}']).when( {{ \n    $any : function(s,e) {{ \n        if(e.body && e.body.{eventProperty}) {{\n            linkTo('{outputStreamCategory}-' + e.body.{eventProperty}, e) \n        }}\n        \n    }}\n}});";
        if ((await register.Get(outputStreamCategory)) != null)
            await Update(client, outputStreamCategory, query);
        else
        {
            await client.CreateContinuousAsync(outputStreamCategory, query, false, cancellationToken: token);
            await client.DisableAsync(outputStreamCategory, cancellationToken: token);
            await client.UpdateAsync(outputStreamCategory, query, true, cancellationToken: token);
            await client.EnableAsync(outputStreamCategory, cancellationToken: token);
        }
    }
    public static async Task EnsureJoinProjection(this EventStoreProjectionManagementClient client,
        string outputStream, IProjectionRegister register, IEnumerable<string> eventTypes, CancellationToken token = default)
    {
        var query = CreateQuery(outputStream, eventTypes);

        if ((await register.Get(outputStream)) != null)
            await Update(client, outputStream, query, token);
        else
        {
            await client.CreateContinuousAsync(outputStream, query, false, cancellationToken: token);
            await client.DisableAsync(outputStream, cancellationToken: token);
            await client.UpdateAsync(outputStream, query, true, cancellationToken: token);
            await client.EnableAsync(outputStream, cancellationToken: token);
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