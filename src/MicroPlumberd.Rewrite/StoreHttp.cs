using System.Net.Http.Headers;
using System.Text;
using MicroPlumberd;

namespace MicroPlumberd.Rewrite;

/// <summary>
/// The tool's HTTP access to a store's management endpoints, over ONE shared client.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> <c>KurrentHttpEndpoint.CreateClient</c> builds a fresh
/// <see cref="HttpClientHandler"/> — and therefore a fresh connection pool — per call. That is fine for the
/// handful of calls the projection copier makes, but the connected-client guard is polled: once per guard
/// evaluation, and every 200 ms while the tool waits for its own connections to drain before the swap. Each
/// one leaked a handler and its sockets.</para>
/// <para>Measured 2026-09-07: the end-to-end suite aborted mid-run after 71 of 76 tests — and
/// <c>dotnet test</c> still printed <c>Passed!</c> for the 71 that had run, which is exactly the
/// "a dead run looks green" failure the CI test-count floor guards against.</para>
/// <para>One client, auth per request. <see cref="HttpClient"/> is thread-safe and is meant to be shared.</para>
/// </remarks>
internal static class StoreHttp
{
    private static readonly HttpClient Client = new(new HttpClientHandler
    {
        // The tool talks to a store it has just been pointed at, over loopback or a docker bridge, usually
        // with a self-signed certificate or none at all.
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    })
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    /// <summary>GETs a path relative to the store's HTTP base, with basic auth.</summary>
    public static Task<HttpResponseMessage> GetAsync(Uri baseUri, string user, string pass, string path,
        CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{user}:{pass}")));
        return Client.SendAsync(request, ct);
    }

    /// <summary>GETs a path using the credentials embedded in a KurrentDB connection string.</summary>
    public static Task<HttpResponseMessage> GetAsync(string connectionString, string path,
        CancellationToken ct = default)
    {
        var (baseUri, user, pass) = KurrentHttpEndpoint.Parse(connectionString);
        return GetAsync(baseUri, user, pass, path, ct);
    }

    /// <summary>The store's HTTP base address, for messages.</summary>
    public static Uri BaseUriOf(string connectionString) => KurrentHttpEndpoint.Parse(connectionString).BaseUri;
}
