using KurrentDB.Client;
using Grpc.Core;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MicroPlumberd;

/// <summary>
/// Provides extension methods for KurrentDBProjectionManagementClient to simplify projection management.
/// </summary>
public static class KurrentDBProjectionManagementClientExtensions
{
    private const int PROJECTION_UPDATE_RETRY_COUNT = 10;

    /// <summary>
    /// A source event the projection runtime could not resolve (its target was scavenged, typically by $maxAge)
    /// arrives with streamId == null and sequenceNumber == -1. Emitting linkTo for such an event writes an
    /// invalid link and faults the projection permanently ("Invalid link to event -1@null"), which takes down
    /// every subscription reading the output stream. Skip those events instead.
    /// Accepted trade-off: a skipped event is never re-linked, so the guard assumes an unresolvable source
    /// event is one whose loss is intended (expired by design).
    /// </summary>
    private const string LinkableEventGuard = "e && e.streamId !== null && e.sequenceNumber >= 0";

    /// <summary>
    /// Attempts to create or update a join projection in the EventStore.
    /// </summary>
    /// <returns><c>true</c> if the projection was created or updated; <c>false</c> if it was already up-to-date and no change was applied.</returns>
    public static async Task<bool> TryCreateJoinProjection(this KurrentDBProjectionManagementClient client,
        KurrentDBClient esClient,
        string outputStream, IEnumerable<string> eventTypes)
    {
        var query = CreateJoinQuery(outputStream, eventTypes);

        if (await client.ListContinuousAsync().AnyAsync(x => x.Name == outputStream))
            return await UpdateIfChanged(client, esClient, outputStream, query);
        else
        {
            await CreateAndStoreHash(client, esClient, outputStream, query);
            return true;
        }
    }

    /// <summary>
    /// Ensures the existence and proper configuration of a lookup projection in the EventStore.
    /// </summary>
    /// <returns><c>true</c> if the projection was created or updated; <c>false</c> if it was already up-to-date and no change was applied.</returns>
    public static async Task<bool> EnsureLookupProjection(this KurrentDBProjectionManagementClient client,
        KurrentDBClient esClient,
        IProjectionRegister register,
        string category, string eventProperty, string outputStreamCategory,
        CancellationToken token = default)
    {
        string query = CreateLookupQuery(category, eventProperty, outputStreamCategory);

        if ((await register.Get(outputStreamCategory)) != null)
            return await UpdateIfChanged(client, esClient, outputStreamCategory, query, token);
        else
        {
            await CreateAndStoreHash(client, esClient, outputStreamCategory, query, token);
            return true;
        }
    }

    /// <summary>
    /// Attempts to create or update a join projection in the EventStore.
    /// </summary>
    /// <returns><c>true</c> if the projection was created or updated; <c>false</c> if it was already up-to-date and no change was applied.</returns>
    public static async Task<bool> TryCreateJoinProjection(this KurrentDBProjectionManagementClient client,
        KurrentDBClient esClient,
        string outputStream, IProjectionRegister register, IEnumerable<string> eventTypes,
        CancellationToken token = default)
    {
        if (!eventTypes.Any())
            throw new ArgumentOutOfRangeException(
                $"There are not event type to create the output stream: {outputStream}");

        var query = CreateJoinQuery(outputStream, eventTypes);

        if ((await register.Get(outputStream)) != null)
            return await UpdateIfChanged(client, esClient, outputStream, query, token);
        else
        {
            await CreateAndStoreHash(client, esClient, outputStream, query, token);
            return true;
        }
    }

    private static async Task<bool> UpdateIfChanged(KurrentDBProjectionManagementClient client, KurrentDBClient esClient,
        string outputStream, string query, CancellationToken token = default)
    {
        var newHash = ProjectionQueryHash.Of(query);
        var (existing, legacy) = await ProjectionQueryHash.ReadWithSourceAsync(esClient, outputStream, token);
        if (existing == newHash)
        {
            // Recognised from the legacy location (the output stream's metadata, which the projection's own
            // writes can erase): move it to its own stream so it cannot be lost there again.
            if (legacy) await ProjectionQueryHash.StoreAsync(esClient, outputStream, newHash, token);
            return false;
        }

        // Changed — or unknown (no hash anywhere, e.g. erased by the projection's first emit under an older
        // MicroPlumberd). Either way the update below is safe: it never disables the projection, and an update
        // with the same query keeps the projection's checkpoint (nothing is linked twice).
        await UpdateWithRetry(client, outputStream, query, token);
        await ProjectionQueryHash.StoreAsync(esClient, outputStream, newHash, token);
        return true;
    }

    private static async Task CreateAndStoreHash(KurrentDBProjectionManagementClient client, KurrentDBClient esClient,
        string outputStream, string query, CancellationToken token = default)
    {
        await client.CreateContinuousAsync(outputStream, query, false, cancellationToken: token);
        // The gRPC create cannot enable emitting; the update does — on the running projection, never after
        // disabling it.
        await UpdateWithRetry(client, outputStream, query, token);
        await ProjectionQueryHash.StoreAsync(esClient, outputStream, ProjectionQueryHash.Of(query), token);
    }

    /// <summary>
    /// Updates the query of a projection WITHOUT disabling it first. It used to disable, then update: when
    /// the update was refused the projection stayed disabled — a read model that silently stops. KurrentDB
    /// accepts an update on a running projection. Should a retried update still fail, the projection is
    /// enabled before the failure propagates, so an ensure can fail but never switch a projection off.
    /// </summary>
    private static async Task UpdateWithRetry(KurrentDBProjectionManagementClient client, string outputStream,
        string query, CancellationToken token)
    {
        for (int i = 0; ; i++)
        {
            try
            {
                await client.UpdateAsync(outputStream, query, true, cancellationToken: token);
                await client.EnableAsync(outputStream, cancellationToken: token);
                return;
            }
            catch (RpcException ex) when (IsTransient(ex) && i < PROJECTION_UPDATE_RETRY_COUNT - 1)
            {
                await Task.Delay(Random.Shared.Next(1000), token);
            }
            catch (Exception) when (!token.IsCancellationRequested)
            {
                try { await client.EnableAsync(outputStream, cancellationToken: token); }
                catch { /* the original failure is the one to report */ }
                throw;
            }
        }
    }

    private static bool IsTransient(RpcException ex) => ex.StatusCode is StatusCode.DeadlineExceeded
        or StatusCode.Unavailable or StatusCode.Aborted or StatusCode.FailedPrecondition;

    internal static string CreateJoinQuery(string outputStream, IEnumerable<string> eventTypes)
    {
        string fromStreamsArg = string.Join(',', eventTypes.Select(x => $"'$et-{x}'"));
        string query = $"fromStreams([{fromStreamsArg}]).when( {{ " +
                       $"\n    $any : function(s,e) {{ if({LinkableEventGuard}) linkTo('{outputStream}', e) }}" +
                       $"\n}});";
        return query;
    }

    internal static string CreateLookupQuery(string category, string eventProperty, string outputStreamCategory) =>
        $"fromStreams(['$ce-{category}']).when( {{ \n    $any : function(s,e) {{ \n        if({LinkableEventGuard} && e.body && e.body.{eventProperty}) {{\n            linkTo('{outputStreamCategory}-' + e.body.{eventProperty}, e) \n        }}\n        \n    }}\n}});";
}
