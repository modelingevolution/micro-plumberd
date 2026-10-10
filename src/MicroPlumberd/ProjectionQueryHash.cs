using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KurrentDB.Client;

namespace MicroPlumberd;

/// <summary>
/// Where MicroPlumberd remembers which query a projection it created runs, so an unchanged query is not
/// pushed again at every start.
/// <para>
/// In the metadata of a stream of its own, <c>mp-projection-{name}</c>, which has no events and which no
/// projection ever writes. It used to live in the projection's OUTPUT stream metadata, where the projection's
/// first emit into a then-empty stream rewrote the metadata (<c>{"$acl":{}}</c>) and the hash was lost.
/// That legacy location is still read, so projections created by older versions are recognised.
/// </para>
/// </summary>
public static class ProjectionQueryHash
{
    internal const string MetadataKey = "mp_query_hash";

    /// <summary>The stream whose metadata holds the hash of projection <paramref name="projection"/>.</summary>
    public static string StreamFor(string projection) => $"mp-projection-{projection}";

    /// <summary>SHA-256 of the query text, upper-case hex.</summary>
    public static string Of(string query) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(query)));

    /// <summary>The stored hash, from its own stream, else from the legacy location; null when neither has one.</summary>
    public static async Task<string?> ReadAsync(KurrentDBClient client, string projection, CancellationToken token = default) =>
        (await ReadWithSourceAsync(client, projection, token)).Hash;

    internal static async Task<(string? Hash, bool Legacy)> ReadWithSourceAsync(KurrentDBClient client, string projection,
        CancellationToken token = default)
    {
        if (await ReadKey(client, StreamFor(projection), token) is { } own) return (own, false);
        return (await ReadKey(client, projection, token), true);
    }

    /// <summary>Stores the hash in the projection's own hash stream (never the output stream).</summary>
    public static async Task StoreAsync(KurrentDBClient client, string projection, string hash, CancellationToken token = default)
    {
        var custom = JsonDocument.Parse($"{{\"{MetadataKey}\":\"{hash}\"}}");
        await client.SetStreamMetadataAsync(StreamFor(projection), StreamState.Any,
            new KurrentDB.Client.StreamMetadata(customMetadata: custom), cancellationToken: token);
    }

    private static async Task<string?> ReadKey(KurrentDBClient client, string stream, CancellationToken token)
    {
        try
        {
            var meta = await client.GetStreamMetadataAsync(stream, cancellationToken: token);
            var custom = meta.Metadata.CustomMetadata;
            if (custom == null || !custom.RootElement.TryGetProperty(MetadataKey, out var v)) return null;
            return v.GetString();
        }
        catch (StreamNotFoundException)
        {
            return null;
        }
    }
}
