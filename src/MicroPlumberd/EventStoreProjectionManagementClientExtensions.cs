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
    private const string QueryHashMetadataKey = "mp_query_hash";
    private const int PROJECTION_UPDATE_RETRY_COUNT = 10;

    /// <summary>
    /// Attempts to create or update a join projection in the EventStore.
    /// </summary>
    /// <returns><c>true</c> if the projection was created or updated; <c>false</c> if it was already up-to-date and no change was applied.</returns>
    public static async Task<bool> TryCreateJoinProjection(this KurrentDBProjectionManagementClient client,
        KurrentDBClient esClient,
        string outputStream, IEnumerable<string> eventTypes)
    {
        var query = CreateQuery(outputStream, eventTypes);

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
        string query =
            $"fromStreams(['$ce-{category}']).when( {{ \n    $any : function(s,e) {{ \n        if(e.body && e.body.{eventProperty}) {{\n            linkTo('{outputStreamCategory}-' + e.body.{eventProperty}, e) \n        }}\n        \n    }}\n}});";

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

        var query = CreateQuery(outputStream, eventTypes);

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
        var newHash = ComputeQueryHash(query);
        var existing = await TryGetStoredQueryHash(esClient, outputStream, token);
        if (existing == newHash) return false;

        await UpdateWithRetry(client, outputStream, query, token);
        await StoreQueryHash(esClient, outputStream, newHash, token);
        return true;
    }

    private static async Task CreateAndStoreHash(KurrentDBProjectionManagementClient client, KurrentDBClient esClient,
        string outputStream, string query, CancellationToken token = default)
    {
        await client.CreateContinuousAsync(outputStream, query, false, cancellationToken: token);
        await client.DisableAsync(outputStream, cancellationToken: token);
        await client.UpdateAsync(outputStream, query, true, cancellationToken: token);
        await client.EnableAsync(outputStream, cancellationToken: token);
        await StoreQueryHash(esClient, outputStream, ComputeQueryHash(query), token);
    }

    private static async Task UpdateWithRetry(KurrentDBProjectionManagementClient client, string outputStream,
        string query, CancellationToken token)
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
    }

    private static string ComputeQueryHash(string query)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(query));
        return Convert.ToHexString(bytes);
    }

    private static async Task<string?> TryGetStoredQueryHash(KurrentDBClient esClient, string outputStream,
        CancellationToken token)
    {
        try
        {
            var meta = await esClient.GetStreamMetadataAsync(outputStream, cancellationToken: token);
            var custom = meta.Metadata.CustomMetadata;
            if (custom == null) return null;
            if (!custom.RootElement.TryGetProperty(QueryHashMetadataKey, out var v)) return null;
            return v.GetString();
        }
        catch
        {
            return null;
        }
    }

    private static async Task StoreQueryHash(KurrentDBClient esClient, string outputStream, string hash,
        CancellationToken token)
    {
        var existing = await esClient.GetStreamMetadataAsync(outputStream, cancellationToken: token);
        var customDoc = JsonDocument.Parse($"{{\"{QueryHashMetadataKey}\":\"{hash}\"}}");
        var newMeta = new KurrentDB.Client.StreamMetadata(
            maxCount: existing.Metadata.MaxCount,
            maxAge: existing.Metadata.MaxAge,
            truncateBefore: existing.Metadata.TruncateBefore,
            cacheControl: existing.Metadata.CacheControl,
            acl: existing.Metadata.Acl,
            customMetadata: customDoc);
        await esClient.SetStreamMetadataAsync(outputStream, StreamState.Any, newMeta,
            cancellationToken: token);
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
