using EventStore.Client;
using Grpc.Core;
using Microsoft.Win32;

namespace MicroPlumberd;

public static class EventStoreProjectionManagementClientExtensions
{
    public static async Task TryCreateJoinProjection(this EventStoreProjectionManagementClient client,
        string outputStream, IEnumerable<string> eventTypes)
    {
        var query = CreateQuery(outputStream, eventTypes);

        if (await client.ListContinuousAsync().AnyAsync(x => x.Name == outputStream))
            await Update(client, outputStream, query);
        else
            await client.CreateContinuousAsync(outputStream, query, true);
    }

    private static async Task Update(EventStoreProjectionManagementClient client, string outputStream, string query,
        CancellationToken token = default)
    {
        for (int i = 0; i < PROJECTION_UPDATE_RETRY_COUNT; i++)
        {
            try
            {
                var state = await client.GetStatusAsync(outputStream, cancellationToken: token);
                if (state!.Status != "Stopped")
                    await client.DisableAsync(outputStream, cancellationToken: token);
                await client.UpdateAsync(outputStream, query, true, cancellationToken: token);
                await client.EnableAsync(outputStream, cancellationToken: token);
                return;
            }
            catch (RpcException ex)
            {
                if (ex.Status.StatusCode != StatusCode.DeadlineExceeded) throw;
                if (i == PROJECTION_UPDATE_RETRY_COUNT - 1)
                    throw;
                
                await Task.Delay(Random.Shared.Next(1000), token);
            }
        }
        // We'll never reach this place, because of if in catch.
    }

    /// <summary>
    /// Ensures the existence and proper configuration of a lookup projection in the EventStore.
    /// </summary>
    /// <param name="client">
    /// The <see cref="EventStoreProjectionManagementClient"/> instance used to manage projections.
    /// </param>
    /// <param name="register">
    /// The <see cref="IProjectionRegister"/> instance used to check the existence of the projection.
    /// </param>
    /// <param name="category">
    /// The category of events to be processed by the projection.
    /// </param>
    /// <param name="eventProperty">
    /// The property of the event used to determine the output stream.
    /// </param>
    /// <param name="outputStreamCategory">
    /// The category of the output stream to which events will be linked.
    /// </param>
    /// <param name="token">
    /// A <see cref="CancellationToken"/> to observe while waiting for the task to complete.
    /// </param>
    /// <returns>
    /// A <see cref="Task"/> representing the asynchronous operation.
    /// </returns>
    /// <remarks>
    /// This method creates a continuous projection in the EventStore that links events from the specified category
    /// to output streams based on the value of the specified event property. If the projection already exists,
    /// it will be updated to ensure it matches the desired configuration.
    /// </remarks>
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

    private const int PROJECTION_UPDATE_RETRY_COUNT = 10;
    /// <summary>
    /// Attempts to create or update a join projection in the EventStore.
    /// </summary>
    /// <param name="client">The <see cref="EventStoreProjectionManagementClient"/> used to manage projections.</param>
    /// <param name="outputStream">The name of the output stream for the join projection.</param>
    /// <param name="register">The projection register used to check for existing projections.</param>
    /// <param name="eventTypes">The collection of event types to include in the join projection.</param>
    /// <param name="token">An optional <see cref="CancellationToken"/> to observe while waiting for the task to complete.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public static async Task TryCreateJoinProjection(this EventStoreProjectionManagementClient client,
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