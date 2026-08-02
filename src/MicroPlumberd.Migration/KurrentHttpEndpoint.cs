using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

namespace MicroPlumberd.Migration;

/// <summary>
/// Derives the HTTP(S) base address + basic-auth credentials of a KurrentDB node from its gRPC/esdb
/// connection string, and builds an <see cref="HttpClient"/> for the management APIs (projections,
/// user-defined indexes) the gRPC client does not expose.
/// </summary>
/// <remarks>
/// Shared by <see cref="ProjectionCopier"/> (reads projection queries over <c>/projection/…/query</c>) and
/// <see cref="UserDefinedIndexSource"/> (creates/inspects indexes over <c>/v2/indexes/…</c>). A single parser
/// keeps the connection-string handling identical for both.
/// </remarks>
internal static class KurrentHttpEndpoint
{
    /// <summary>Parses the first host + credentials + TLS flag out of a KurrentDB connection string.</summary>
    public static (Uri BaseUri, string User, string Pass) Parse(string connectionString)
    {
        var m = Regex.Match(connectionString,
            @"^(?<scheme>[a-zA-Z0-9+]+)://(?:(?<user>[^:@/]+):(?<pass>[^@/]+)@)?(?<hosts>[^/?]+)(?:/[^?]*)?(?:\?(?<query>.*))?$");
        if (!m.Success)
            throw new ArgumentException($"Unrecognised KurrentDB connection string: '{connectionString}'.");

        var user = m.Groups["user"].Success ? m.Groups["user"].Value : "admin";
        var pass = m.Groups["pass"].Success ? m.Groups["pass"].Value : "changeit";
        var firstHost = m.Groups["hosts"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
        if (!firstHost.Contains(':')) firstHost += ":2113";

        var tls = true;
        if (m.Groups["query"].Success)
            foreach (var kv in m.Groups["query"].Value.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = kv.Split('=', 2);
                if (parts.Length == 2 && parts[0].Equals("tls", StringComparison.OrdinalIgnoreCase))
                    tls = !parts[1].Equals("false", StringComparison.OrdinalIgnoreCase);
            }

        return (new Uri($"{(tls ? "https" : "http")}://{firstHost}/"), user, pass);
    }

    /// <summary>Builds an <see cref="HttpClient"/> with basic auth that accepts the node's dev certificate.</summary>
    public static HttpClient CreateClient(string user, string pass)
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        var http = new HttpClient(handler);
        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{user}:{pass}"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
        return http;
    }
}
