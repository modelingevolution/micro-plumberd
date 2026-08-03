using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using KurrentDB.Client;

namespace MicroPlumberd;

/// <summary>
/// Derives the HTTP(S) base address + basic-auth credentials of a KurrentDB node from its gRPC/esdb
/// connection string (or from an already-parsed <see cref="KurrentDBClientSettings"/>), and builds an
/// <see cref="HttpClient"/> for the management APIs (user-defined indexes over <c>/v2/indexes</c>) the
/// gRPC client does not expose.
/// </summary>
/// <remarks>
/// Relocated into core (from <c>MicroPlumberd.Migration</c>) so the LIVE index-backed subscription path and the
/// offline migration path share ONE endpoint-parsing implementation (see
/// <c>docs/design-index-backed-merge-streams.md</c> — "Assembly layering"). The offline path parses a connection
/// string via <see cref="Parse"/>; the live path derives the same triple from the running engine's
/// <see cref="KurrentDBClientSettings"/> via <see cref="FromSettings"/>.
/// </remarks>
public static class KurrentHttpEndpoint
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

    /// <summary>
    /// Derives the HTTP management base address + basic-auth credentials from an already-configured
    /// <see cref="KurrentDBClientSettings"/> (the shape the running <see cref="PlumberEngine"/> holds). Uses the
    /// same node address the gRPC client connects to (<c>ConnectivitySettings.Address</c>, or the first gossip
    /// seed when no explicit address is set) so the index HTTP calls and the gRPC reads target the same node.
    /// </summary>
    public static (Uri BaseUri, string User, string Pass) FromSettings(KurrentDBClientSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var cs = settings.ConnectivitySettings;
        var address = cs?.Address;
        if (address is null)
        {
            var seed = cs?.GossipSeeds is { Length: > 0 } seeds ? seeds[0] : null;
            if (seed is null)
                throw new InvalidOperationException(
                    "Cannot derive a KurrentDB HTTP endpoint: the client settings have neither ConnectivitySettings.Address "
                    + "nor a gossip seed. Index-backed merge needs the node's HTTP address to manage /v2/indexes.");
            var insecure = cs?.Insecure ?? false;
            address = new Uri($"{(insecure ? "http" : "https")}://{seed}/");
        }

        // Keep only scheme://host:port/ — the /v2/indexes URIs are resolved relative to this base.
        var baseUri = new Uri(address.GetLeftPart(UriPartial.Authority) + "/");
        var user = settings.DefaultCredentials?.Username ?? "admin";
        var pass = settings.DefaultCredentials?.Password ?? "changeit";
        return (baseUri, user, pass);
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
