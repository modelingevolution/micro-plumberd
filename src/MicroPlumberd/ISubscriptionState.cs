using KurrentDB.Client;

namespace MicroPlumberd;

/// <summary>
/// The seam that lets ONE subscription loop (<c>SubscriptionRunner.WithHandler</c>) serve both merge sources —
/// a projection-backed output stream (<c>StreamSubscriptionState</c>, via <c>SubscribeToStream</c>) and a
/// KurrentDB user-defined index (<see cref="IndexSubscriptionState"/>, via filtered <c>SubscribeToAll</c>). The
/// only per-source differences are which subscribe call is made and how the resume position is computed from a
/// delivered <see cref="ResolvedEvent"/>; both are hidden here so the loop body (dispatch, <c>CaughtUp</c>,
/// resubscribe-with-backoff) is literally reused, not forked. See
/// <c>docs/design-index-backed-merge-streams.md</c> — "Why the loop is shared, not forked".
/// </summary>
interface ISubscriptionState : IDisposable
{
    /// <summary>Human-readable name of the subscribed source (for logs / operation context).</summary>
    string StreamName { get; }

    /// <summary>Cancellation token that stops the subscription loop.</summary>
    CancellationToken CancellationToken { get; }

    /// <summary>Opens the subscription from the current resume position (SubscribeToStream OR filtered SubscribeToAll).</summary>
    KurrentDBClient.StreamSubscriptionResult Subscribe();

    /// <summary>Advances the in-memory resume position after an event was successfully dispatched.</summary>
    void Advance(ResolvedEvent e);
}

/// <summary>
/// Index-backed merge subscription state (catch-up, client-checkpointed). Subscribes to filtered <c>$all</c> over
/// <c>StreamFilter.Prefix($idx-user-&lt;name&gt;)</c> with <c>resolveLinkTos:true</c>, resumes from the last
/// delivered link's <c>$all</c> <see cref="Position"/> (<c>FromAll.After(pos)</c>) — the ONLY working mechanism
/// (SPIKE-2b/SPIKE-3/SPIKE-9: a direct <c>SubscribeToStream</c> on the index name silently delivers nothing).
/// </summary>
sealed class IndexSubscriptionState : ISubscriptionState
{
    private readonly KurrentDBClient _client;
    private readonly SubscriptionFilterOptions _filter;
    private readonly UserCredentials? _userCredentials;
    private IDisposable? _subscription;
    private FromAll _position;

    /// <param name="client">The gRPC client used for the filtered <c>$all</c> subscription.</param>
    /// <param name="indexStream">The index read stream — <c>$idx-user-&lt;name&gt;</c> — used as the prefix filter.</param>
    /// <param name="start">Boot start position (<c>FromAll.Start</c> rebuilds; <c>FromAll.End</c> tails only).</param>
    /// <param name="userCredentials">Optional per-subscription credentials.</param>
    /// <param name="cancellationToken">Stops the subscription loop.</param>
    public IndexSubscriptionState(KurrentDBClient client, string indexStream, FromAll start,
        UserCredentials? userCredentials, CancellationToken cancellationToken)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        StreamName = indexStream ?? throw new ArgumentNullException(nameof(indexStream));
        _filter = new SubscriptionFilterOptions(StreamFilter.Prefix(indexStream));
        _position = start;
        _userCredentials = userCredentials;
        CancellationToken = cancellationToken;
    }

    public string StreamName { get; }

    public CancellationToken CancellationToken { get; }

    public KurrentDBClient.StreamSubscriptionResult Subscribe()
    {
        var result = _client.SubscribeToAll(_position, resolveLinkTos: true, filterOptions: _filter,
            userCredentials: _userCredentials, cancellationToken: CancellationToken);
        _subscription = result;
        return result;
    }

    public void Advance(ResolvedEvent e)
    {
        // The resume key is the link's $all Position — NOT an index-stream revision (SPIKE-3). Resolved reads may
        // carry a null OriginalPosition for some system frames; keep the last known position in that case.
        if (e.OriginalPosition is { } p) _position = FromAll.After(p);
    }

    public void Dispose() => _subscription?.Dispose();
}
