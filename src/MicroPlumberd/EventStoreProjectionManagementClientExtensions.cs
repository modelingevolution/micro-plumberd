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
            return await CreateAndStoreHash(client, esClient, outputStream, query);
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

        return await Ensure(client, esClient, register, outputStreamCategory, query, null, token);
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

        return await TryCreateJoinProjectionBy(client, esClient, outputStream, register, eventTypes, null, token);
    }

    /// <summary>As <see cref="TryCreateJoinProjection"/>, naming the model type that defines it (for the one-definition refusal).</summary>
    internal static async Task<bool> TryCreateJoinProjectionBy(KurrentDBProjectionManagementClient client, KurrentDBClient esClient,
        string outputStream, IProjectionRegister register, IEnumerable<string> eventTypes, Type? definedBy,
        CancellationToken token = default)
    {
        if (!eventTypes.Any())
            throw new ArgumentOutOfRangeException(
                $"There are not event type to create the output stream: {outputStream}");

        var query = CreateJoinQuery(outputStream, eventTypes);

        return await Ensure(client, esClient, register, outputStream, query, definedBy, token);
    }

    /// <summary>
    /// One ensure. Within one process run an output stream has ONE definition: a stream this process already ensured is
    /// decided by that (the same hash: no change; a different one: two code paths define it differently — surfaced, never
    /// overwritten, #29). Otherwise the store decides: a projection the register lists is updated if its definition changed
    /// (a new deployment), and one it does not list is created — after which the register, loaded before it existed, is
    /// dropped so it sees it (card microplumberd-projection-register-invalidation, the #29 root cause).
    /// </summary>
    private static async Task<bool> Ensure(KurrentDBProjectionManagementClient client, KurrentDBClient esClient,
        IProjectionRegister register, string outputStream, string query, Type? definedBy, CancellationToken token)
    {
        var mine = ProjectionRegister.Definition.Of(ProjectionQueryHash.Of(query), definedBy);
        var here = register as ProjectionRegister;
        if (here?.DefinedHere(outputStream) is { } earlier)
            return earlier.Hash == mine.Hash ? false : throw DefinedTwice(outputStream, earlier, mine);

        bool changed;
        if ((await register.Get(outputStream)) != null)
            changed = await UpdateIfChanged(client, esClient, outputStream, query, token);
        else
        {
            // A FAILED create invalidates too: what the store holds is then unknown, and a reload is the only honest answer.
            try { changed = await CreateAndStoreHash(client, esClient, outputStream, query, token); }
            finally { here?.Invalidate(); }
        }

        if (here?.Defined(outputStream, mine) is { } first && first.Hash != mine.Hash)
            throw DefinedTwice(outputStream, first, mine);
        return changed;
    }

    /// <summary>The one-definition refusal, naming BOTH definers (rev-reach 9499dc2cb): a short-name clash is then obvious.</summary>
    private static InvalidOperationException DefinedTwice(string outputStream, ProjectionRegister.Definition first,
        ProjectionRegister.Definition second) =>
        new($"Projection '{outputStream}' was already defined in this process by {first.By} (query hash {first.Hash}); " +
            $"{second.By} gives it a DIFFERENT definition (query hash {second.Hash}). It was not overwritten. Two code paths " +
            "define the same output stream differently — e.g. two model types whose short names map to the same stream.");

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

    /// <summary>How long a create that met an existing projection waits for that projection's creator to record its query hash.</summary>
    internal static TimeSpan ConflictHashWait = TimeSpan.FromSeconds(10);

    /// <summary>Test seam, keyed by the client: runs just before a conditional hash claim (a rival's store lands here).</summary>
    internal static Func<KurrentDBClient, string, Task>? BeforeHashClaimForTests;

    /// <summary>Test seam, keyed by the client: observes every query update this library makes.</summary>
    internal static Action<KurrentDBProjectionManagementClient, string>? OnQueryUpdateForTests;

    /// <returns><c>true</c> if this call created (or, for an unknown existing one, updated) the projection; <c>false</c> if an
    /// IDENTICAL projection already existed.</returns>
    private static async Task<bool> CreateAndStoreHash(KurrentDBProjectionManagementClient client, KurrentDBClient esClient,
        string outputStream, string query, CancellationToken token = default)
    {
        // Read BEFORE the create (rev-reach e3712ab61 M1): if the create SUCCEEDS this process IS the creator — a racing creator
        // gets the Conflict — so any hash already there is STALE (a projection deleted to be redefined leaves its hash stream
        // behind) and must be overwritten, not obeyed. The claim below stores AT this revision: a stale hash is replaced, and
        // only a rival that stored BETWEEN the create and the claim moves the revision and is compared.
        var (_, before) = await ProjectionQueryHash.ReadStateAsync(esClient, outputStream, token);
        try
        {
            await client.CreateContinuousAsync(outputStream, query, false, cancellationToken: token);
        }
        catch (RpcException ex) when (IsConflict(ex))
        {
            // The projection EXISTS (issue #29): the register is loaded once per process and does not see a projection
            // created after it loaded — a second ensure of the same model in the same process, or another instance's
            // creation — so the create path met it. Decided by the definition, never by overwriting it.
            return await ResolveExisting(client, esClient, outputStream, query, ex, token);
        }
        // The gRPC create cannot enable emitting; the update does — on the running projection, never after
        // disabling it.
        await UpdateWithRetry(client, outputStream, query, token);
        var mine = ProjectionQueryHash.Of(query);
        if (await ClaimHash(esClient, outputStream, mine, before, token) is { } other && other != mine)
            throw new InvalidOperationException(
                $"Projection '{outputStream}' was given a DIFFERENT definition (query hash {other}, this one {mine}) while this " +
                "one was being created; its hash was not overwritten. Two code paths define the same output stream differently.");
        return true;
    }

    /// <summary>
    /// Stores <paramref name="mine"/> only if the hash stream is still at <paramref name="expected"/>. Null when the claim WON;
    /// otherwise the hash that is there now (another writer stored first).
    /// </summary>
    private static async Task<string?> ClaimHash(KurrentDBClient esClient, string outputStream, string mine, StreamState expected,
        CancellationToken token)
    {
        if (BeforeHashClaimForTests is { } seam) await seam(esClient, outputStream);
        try
        {
            await ProjectionQueryHash.StoreAsync(esClient, outputStream, mine, expected, token);
            return null;
        }
        catch (WrongExpectedVersionException)
        {
            return (await ProjectionQueryHash.ReadStateAsync(esClient, outputStream, token)).Hash
                   ?? (await ProjectionQueryHash.ReadWithSourceAsync(esClient, outputStream, token)).Hash;
        }
    }

    /// <summary>
    /// A create refused because the projection already exists. Its creator records the query hash after creating it, so
    /// this waits (bounded) for that hash: the SAME definition is success — nothing to do; a DIFFERENT one surfaces, named,
    /// and is never overwritten. No hash within the wait (a creator that died between creating and recording, or an older
    /// MicroPlumberd) is CLAIMED first and only then updated in place (rev-reach e3712ab61 m2): a refused claim means another
    /// definition got there, and this one must not have written its query either.
    /// </summary>
    private static async Task<bool> ResolveExisting(KurrentDBProjectionManagementClient client, KurrentDBClient esClient,
        string outputStream, string query, RpcException conflict, CancellationToken token)
    {
        var mine = ProjectionQueryHash.Of(query);
        var deadline = DateTime.UtcNow + ConflictHashWait;
        StreamState observed;
        while (true)
        {
            var (own, expected) = await ProjectionQueryHash.ReadStateAsync(esClient, outputStream, token);
            var existing = own ?? (await ProjectionQueryHash.ReadWithSourceAsync(esClient, outputStream, token)).Hash;
            if (existing == mine) return false;
            if (existing != null) throw Different(outputStream, existing, mine, conflict);
            observed = expected;
            if (DateTime.UtcNow >= deadline) break;
            await Task.Delay(200, token);
        }

        if (await ClaimHash(esClient, outputStream, mine, observed, token) is { } other)
            return other == mine ? false : throw Different(outputStream, other, mine, conflict);
        await UpdateWithRetry(client, outputStream, query, token);
        return true;
    }

    private static InvalidOperationException Different(string outputStream, string existing, string mine, Exception cause) =>
        new($"Projection '{outputStream}' already exists with a DIFFERENT definition (query hash {existing}, this one {mine}); " +
            "it was not overwritten. Two code paths define the same output stream differently.", cause);

    /// <summary>
    /// The Detail word KurrentDB uses for a create of an existing projection: "Envelope callback expected Updated, received
    /// Conflict". Measured: KurrentDB.Client reports it with <see cref="StatusCode.Unknown"/>, NOT AlreadyExists, so THIS match
    /// is what fires today (pinned by JoinProjectionConflictTests against a real store; the client has no Conflict enum).
    /// </summary>
    // Measured on KurrentDB.Client 1.6.0 (Unknown + this Detail): a client bump must re-run that row and re-measure.
    internal const string ConflictDetail = "received Conflict";

    /// <summary>The KurrentDB.Client version <see cref="ConflictDetail"/>'s shape was measured on; KurrentDBClientPinTests holds the pin to it.</summary>
    internal const string MeasuredOnClientVersion = "1.6.0";

    /// <summary>A create refused because the projection exists: the measured Detail, or AlreadyExists from a client that reports it.</summary>
    internal static bool IsConflict(RpcException ex) =>
        ex.StatusCode == StatusCode.AlreadyExists || (ex.Status.Detail ?? "").Contains(ConflictDetail, StringComparison.Ordinal);

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
                OnQueryUpdateForTests?.Invoke(client, outputStream);
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
