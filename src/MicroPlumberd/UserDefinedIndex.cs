using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MicroPlumberd;

/// <summary>
/// The relocated, single implementation of the KurrentDB 26.1 USER-DEFINED INDEX lifecycle primitives shared by
/// the LIVE index-backed merge subscription path (core) and the offline migration reader
/// (<c>MicroPlumberd.Migration.UserDefinedIndexSource</c>, which delegates here). Creates/ensures an index over a
/// set of event types (<see cref="EnsureAsync"/>), deletes a superseded one (<see cref="DeleteAsync"/>), reads a
/// stored filter (<see cref="GetFilterAsync"/>) and lists index names (<see cref="ListNamesAsync"/>), plus the
/// pure name/filter/stream helpers (<see cref="NormalizeName"/>, <see cref="BuildFilter"/>,
/// <see cref="IndexStream"/>, <see cref="IndexNameFor"/>).
/// </summary>
/// <remarks>
/// <para><b>Read the index via filtered <c>$all</c> only.</b> An index's links live in <c>$all</c>; the
/// <c>$idx-user-{name}</c> name is NOT a directly subscribable/readable stream (SPIKE-2a/SPIKE-9). Consume it via
/// <c>SubscribeToAll</c>/<c>ReadAllAsync</c> + <see cref="KurrentDB.Client.StreamFilter.Prefix(string)"/> —
/// <see cref="IndexSubscriptionState"/> does exactly this, and <c>StreamSubscriptionState</c> guards against the
/// silent-zero footgun.</para>
/// <para><b>Creation is idempotent.</b> <see cref="EnsureAsync"/> POSTs <c>/v2/indexes/{name}</c>; an existing
/// index (HTTP 409 <c>INDEX_ALREADY_EXISTS</c>, or an identical 200) is a no-op. A DIFFERENT filter on an
/// existing name is left as-is with a logged warning — KurrentDB does not redefine an index in place (SPIKE-5:
/// re-POST → 409), so a filter change means a NEW name (see <see cref="IndexNameFor"/> +
/// <c>UserDefinedIndexReconciler</c>).</para>
/// <para>Every seam logs (index name + the failing URI on error) via the injected <see cref="ILogger"/>,
/// defaulting to <see cref="NullLogger"/> so the type is safe to construct anywhere (incl. WASM: no
/// reload-on-change, no ambient statics).</para>
/// </remarks>
public sealed class UserDefinedIndex
{
    /// <summary>The fixed prefix of an index read stream: <c>$idx-user-</c>.</summary>
    public const string IndexStreamPrefixRoot = "$idx-user-";

    /// <summary>The prefix of a reconciler-managed index NAME: <c>mpidx-</c> (before the normalized output + hash).</summary>
    public const string ManagedIndexNamePrefix = "mpidx-";

    private readonly Uri _httpBase;
    private readonly string _user;
    private readonly string _pass;
    private readonly ILogger _logger;

    /// <summary>
    /// </summary>
    /// <param name="httpBase">The node HTTP(S) management base — see <see cref="KurrentHttpEndpoint"/>.</param>
    /// <param name="user">Basic-auth user for the management API.</param>
    /// <param name="pass">Basic-auth password for the management API.</param>
    /// <param name="logger">Optional; defaults to <see cref="NullLogger"/>.</param>
    public UserDefinedIndex(Uri httpBase, string user, string pass, ILogger? logger = null)
    {
        _httpBase = httpBase ?? throw new ArgumentNullException(nameof(httpBase));
        _user = user ?? throw new ArgumentNullException(nameof(user));
        _pass = pass ?? throw new ArgumentNullException(nameof(pass));
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>The read stream (<c>$idx-user-{name}</c>) that carries <paramref name="name"/>'s indexed links.</summary>
    public static string IndexStream(string name) => IndexStreamPrefixRoot + name;

    /// <summary>
    /// Creates the index if it does not already exist (idempotent). Returns without waiting for the backfill —
    /// the LIVE path subscribes-then-tails immediately (no count gate); the offline path calls its own
    /// count-convergence readiness gate. No-op when <paramref name="dryRun"/> is true.
    /// </summary>
    public async Task EnsureAsync(string name, IReadOnlySet<string> eventTypes, bool dryRun = false,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(eventTypes);
        name = NormalizeName(name);
        var filter = BuildFilter(eventTypes);

        if (dryRun)
        {
            _logger.LogInformation("DRY RUN: would create user-defined index '{Name}' with filter [{Filter}].",
                name, filter);
            return;
        }

        var url = new Uri(_httpBase, $"v2/indexes/{Uri.EscapeDataString(name)}");
        using var http = KurrentHttpEndpoint.CreateClient(_user, _pass);
        var payload = new JsonObject { ["filter"] = filter, ["start"] = true };
        using var content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

        HttpResponseMessage resp;
        try { resp = await http.PostAsync(url, content, ct).ConfigureAwait(false); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to POST user-defined index '{Name}' at {Uri}.", name, url);
            throw;
        }

        if (resp.IsSuccessStatusCode)
        {
            _logger.LogInformation("Created (or confirmed) user-defined index '{Name}' with filter [{Filter}].",
                name, filter);
            return;
        }

        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.Conflict) // 409 INDEX_ALREADY_EXISTS — idempotent success
        {
            await WarnOnFilterDriftAsync(http, name, filter, ct).ConfigureAwait(false);
            _logger.LogInformation("User-defined index '{Name}' already exists — reusing it.", name);
            return;
        }

        _logger.LogError("Creating user-defined index '{Name}' at {Uri} failed: HTTP {Code} {Body}.",
            name, url, (int)resp.StatusCode, body);
        throw new InvalidOperationException(
            $"Creating user-defined index '{name}' failed: HTTP {(int)resp.StatusCode} at {url} — {body}");
    }

    /// <summary>
    /// Deletes an index by name (idempotent — a missing index is treated as already-deleted). Returns
    /// <c>true</c> when the server confirmed the delete (HTTP 2xx) or the index was already gone (404).
    /// Used by the reconciler to retire a superseded (old-hash) index after a definition change.
    /// </summary>
    public async Task<bool> DeleteAsync(string name, CancellationToken ct = default)
    {
        name = NormalizeName(name);
        var url = new Uri(_httpBase, $"v2/indexes/{Uri.EscapeDataString(name)}");
        using var http = KurrentHttpEndpoint.CreateClient(_user, _pass);

        HttpResponseMessage resp;
        try { resp = await http.DeleteAsync(url, ct).ConfigureAwait(false); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to DELETE user-defined index '{Name}' at {Uri}.", name, url);
            return false;
        }

        if (resp.IsSuccessStatusCode || resp.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogInformation("Deleted user-defined index '{Name}' (HTTP {Code}).", name, (int)resp.StatusCode);
            return true;
        }

        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        _logger.LogWarning("Deleting user-defined index '{Name}' at {Uri} failed: HTTP {Code} {Body}.",
            name, url, (int)resp.StatusCode, body);
        return false;
    }

    /// <summary>Reads the stored filter for an index, or <c>null</c> when the index does not exist / has no filter.</summary>
    public async Task<string?> GetFilterAsync(string name, CancellationToken ct = default)
    {
        name = NormalizeName(name);
        var url = new Uri(_httpBase, $"v2/indexes/{Uri.EscapeDataString(name)}");
        using var http = KurrentHttpEndpoint.CreateClient(_user, _pass);
        try
        {
            using var resp = await http.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return JsonNode.Parse(json)?["index"]?["filter"]?.GetValue<string>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read filter for user-defined index '{Name}' at {Uri}.", name, url);
            return null;
        }
    }

    /// <summary>
    /// Lists the names of all user-defined indexes on the node (best-effort — an unparseable or missing list
    /// endpoint logs a warning and yields nothing, since orphan cleanup is best-effort per design). Used by the
    /// reconciler to find superseded <c>mpidx-&lt;output&gt;-&lt;hash&gt;</c> indexes to delete.
    /// </summary>
    public async Task<IReadOnlyList<string>> ListNamesAsync(CancellationToken ct = default)
    {
        var url = new Uri(_httpBase, "v2/indexes");
        using var http = KurrentHttpEndpoint.CreateClient(_user, _pass);
        try
        {
            using var resp = await http.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Listing user-defined indexes at {Uri} returned HTTP {Code}; orphan cleanup skipped.",
                    url, (int)resp.StatusCode);
                return Array.Empty<string>();
            }
            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ParseIndexNames(JsonNode.Parse(json));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not list user-defined indexes at {Uri}; orphan cleanup skipped.", url);
            return Array.Empty<string>();
        }
    }

    // Tolerant parse: the list endpoint may return {"indexes":[{"name":..}]}, {"indexes":["name",..]} or a bare
    // array. Collect any "name" fields and/or bare string entries under a top-level array-ish node.
    private static IReadOnlyList<string> ParseIndexNames(JsonNode? root)
    {
        var names = new List<string>();
        JsonArray? arr = root as JsonArray
                         ?? root?["indexes"] as JsonArray
                         ?? root?["index"] as JsonArray;
        if (arr is null) return names;
        foreach (var item in arr)
        {
            switch (item)
            {
                case JsonValue v when v.TryGetValue<string>(out var s) && !string.IsNullOrEmpty(s):
                    names.Add(s);
                    break;
                case JsonObject o when o["name"]?.GetValue<string>() is { Length: > 0 } n:
                    names.Add(n);
                    break;
            }
        }
        return names;
    }

    // ---- pure helpers --------------------------------------------------------------------------------------

    /// <summary>
    /// The reconciler-managed index NAME for a merge: <c>mpidx-&lt;normalized-output&gt;-&lt;hash8-of-filter&gt;</c>.
    /// <c>hash8</c> is the first 8 lower-hex chars of <c>SHA-256(filter)</c> (the filter is ordinal-sorted, so it is
    /// STABLE for a given event-type SET). Hashing the FILTER (not the output-stream name) is what makes a changed
    /// event-type set deterministically produce a NEW name — the trigger for create-new-and-swap.
    /// </summary>
    public static string IndexNameFor(string outputStream, string? filter)
    {
        var norm = NormalizeName(outputStream);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(filter ?? string.Empty));
        var hash8 = Convert.ToHexString(bytes, 0, 4).ToLowerInvariant(); // 4 bytes -> 8 hex chars
        return $"{ManagedIndexNamePrefix}{norm}-{hash8}";
    }

    /// <summary>The <c>mpidx-&lt;normalized-output&gt;-</c> prefix shared by every managed index for one output stream.</summary>
    public static string ManagedNamePrefixFor(string outputStream) =>
        $"{ManagedIndexNamePrefix}{NormalizeName(outputStream)}-";

    // Process-wide owner of each managed-index BASE (the normalized output stream). Guards the data-destructive
    // punctuation-collision edge below.
    private static readonly ConcurrentDictionary<string, string> _managedBaseOwners = new(StringComparer.Ordinal);

    /// <summary>
    /// Claims the managed-index BASE (the normalized output stream) for <paramref name="outputStream"/> in this
    /// process, throwing if a DIFFERENT output stream already owns the same base. A punctuation collision — e.g.
    /// <c>"Foo.1"</c> and <c>"Foo-1"</c> both normalize to <c>"foo-1"</c> — would make two read models share the
    /// <c>mpidx-&lt;base&gt;-*</c> prefix, so orphan-cleanup for one could DELETE the other's live index. This makes
    /// that fail LOUD at reconcile instead of silently deleting the wrong index. Idempotent for the SAME output
    /// stream (re-reconciling one read model never collides with itself).
    /// </summary>
    public static void RegisterManagedBase(string outputStream)
    {
        if (string.IsNullOrWhiteSpace(outputStream))
            throw new ArgumentException("Output stream must be non-empty.", nameof(outputStream));
        var baseName = NormalizeName(outputStream);
        var owner = _managedBaseOwners.GetOrAdd(baseName, outputStream);
        if (!string.Equals(owner, outputStream, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Managed user-defined-index name collision: output streams '{owner}' and '{outputStream}' both "
                + $"normalize to '{baseName}', so their managed indexes ('{ManagedIndexNamePrefix}{baseName}-*') "
                + "would collide and orphan-cleanup could DELETE the wrong read model's index. Rename one output "
                + "stream so they normalize distinctly.");
    }

    /// <summary>
    /// Builds the index filter as the single-argument JavaScript arrow function KurrentDB requires:
    /// <c>rec =&gt; rec.schema.name == "A" || rec.schema.name == "B"</c>. An empty set ⇒ null (index all records).
    /// Ordinal-sorted so the filter (and thus its hash) is STABLE for a given set; each name is JS-escaped so a
    /// quote/backslash/control char cannot break out of the literal.
    /// </summary>
    public static string? BuildFilter(IReadOnlySet<string> eventTypes)
    {
        ArgumentNullException.ThrowIfNull(eventTypes);
        if (eventTypes.Count == 0) return null;
        var terms = eventTypes.OrderBy(t => t, StringComparer.Ordinal)
            .Select(t => $"rec.schema.name == \"{EscapeJsString(t)}\"");
        return "rec => " + string.Join(" || ", terms);
    }

    /// <summary>
    /// Normalises an arbitrary name (e.g. an <c>[OutputStream]</c> id like <c>FooModel_v1</c>) to a valid KurrentDB
    /// index name: lower-case; every character outside <c>[a-z0-9_]</c> becomes <c>-</c>. Idempotent on names it
    /// already produced (so re-normalising a <c>mpidx-…</c> name is a no-op).
    /// </summary>
    public static string NormalizeName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) throw new ArgumentException("Index name must be non-empty.", nameof(raw));
        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            var c = char.ToLowerInvariant(ch);
            sb.Append(c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '-' ? c : '-');
        }
        return sb.ToString();
    }

    /// <summary>Escapes a string for a JavaScript double-quoted literal (backslash, quote, control chars).</summary>
    internal static string EscapeJsString(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        foreach (var ch in s)
            sb.Append(ch switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                < ' ' => $"\\u{(int)ch:x4}",
                _ => ch.ToString()
            });
        return sb.ToString();
    }

    private async Task WarnOnFilterDriftAsync(HttpClient http, string name, string? requestedFilter, CancellationToken ct)
    {
        var url = new Uri(_httpBase, $"v2/indexes/{Uri.EscapeDataString(name)}");
        try
        {
            using var resp = await http.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return;
            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var existing = JsonNode.Parse(json)?["index"]?["filter"]?.GetValue<string>();
            var existingNorm = string.IsNullOrEmpty(existing) ? null : existing;
            if (!string.Equals(existingNorm, requestedFilter, StringComparison.Ordinal))
                _logger.LogWarning(
                    "User-defined index '{Name}' already exists with a DIFFERENT filter (existing [{Existing}], "
                    + "requested [{Requested}]) — the existing definition is kept.", name, existingNorm ?? "<all>",
                    requestedFilter ?? "<all>");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read existing filter for user-defined index '{Name}'.", name);
        }
    }
}
