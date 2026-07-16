namespace MicroPlumberd.Services.Uniqueness;

/// <summary>
/// Set-wide uniqueness for <typeparamref name="TCategory"/>, using the reservation (try/confirm/cancel)
/// pattern. An event store enforces invariants within one stream; uniqueness spans streams, so the hard
/// lock lives in a relational unique index alongside it.
/// </summary>
/// <remarks>
/// Intended write sequence:
/// <code>
/// await unique.Reserve(nip, companyId);      // 1. take the name on a lease (throws if held)
/// try { await plumber.SaveNew(aggregate); }  // 2. persist the aggregate
/// catch { await unique.RollbackReservation(companyId); throw; }
/// await unique.Confirm(companyId);           // 3. make the reservation permanent
/// </code>
/// If the process dies between 1 and 3 the reservation is left unconfirmed and expires, so the name
/// frees itself — no distributed transaction, and a crash costs the lease duration, not the name.
/// <para>
/// The ordering is deliberate and must not be "optimised" to Reserve → Confirm → SaveNew: a crash
/// between Confirm and SaveNew burns the name permanently, because confirmed rows never expire.
/// </para>
/// <para>
/// The lease buys liveness, not safety — see <see cref="Confirm"/> for the obligation that creates.
/// </para>
/// <para>
/// <b>Names are compared exactly (ordinal/binary), always, on every provider.</b> Normalise before calling
/// if you want case-insensitive uniqueness. There is deliberately no per-category case sensitivity: it
/// would be per-provider collation config, which is precisely how the same two names come to collide on
/// one database and not another. Normalisation is domain knowledge this library must not guess —
/// ToLowerInvariant and culture-aware casing disagree (Turkish dotless i), and Unicode folding has choices
/// only the caller can make.
/// </para>
/// </remarks>
public interface IUniqueNameReservation<TCategory>
{
    /// <summary>Reserve <paramref name="name"/> for <paramref name="source"/> on a lease.
    /// Idempotent for the same source. Throws <see cref="UniqueNameConflictException"/> if a different
    /// source holds it (confirmed, or unconfirmed and unexpired).</summary>
    /// <param name="name">The name to take. Compared exactly (ordinal/binary) on every provider; normalise
    /// before calling if you want case-insensitive uniqueness.</param>
    /// <param name="source">The aggregate id taking the name.</param>
    /// <param name="duration">Lease length; defaults to 10 minutes. Size it to swamp the worst-case time
    /// to persist the aggregate — this is a safety parameter, not a tuning knob.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task Reserve(string name, Guid source, TimeSpan? duration = null, CancellationToken cancellationToken = default);

    /// <summary>Reserve without throwing. Returns false when a different source holds the name.</summary>
    /// <remarks>Returns false ONLY for an actual conflict. Infrastructure failures (deadlock, timeout,
    /// connection loss) propagate as exceptions — they are never reported as "lost the race".</remarks>
    /// <param name="name">The name to take. Compared exactly (ordinal/binary) on every provider; normalise
    /// before calling if you want case-insensitive uniqueness.</param>
    /// <param name="source">The aggregate id taking the name.</param>
    /// <param name="duration">Lease length; defaults to 10 minutes.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<bool> TryReserve(string name, Guid source, TimeSpan? duration = null, CancellationToken cancellationToken = default);

    /// <summary>Make the source's pending reservation permanent, releasing any previous confirmed name
    /// it held (rename). Idempotent. Throws if the lease expired or there is nothing to confirm.</summary>
    /// <remarks>
    /// <b>Caller obligation: if this throws, the aggregate persisted at step 2 is already durable and MUST
    /// be compensated</b> (emit a rejection, soft-delete — whatever the model calls for). Do not swallow
    /// this exception, and do not retry it blindly.
    /// <para>
    /// Why: the lease buys liveness, not safety. If this source stalls past its lease, another source may
    /// collect the dead row and take the name; this source's own <c>SaveNew</c> still succeeds, because the
    /// event store knows nothing of reservations — there is no enforcement point, and a fencing token would
    /// not create one. At that instant two aggregates carry one unique value, and this exception is the ONLY
    /// thing reporting it. Silence here is the worst possible outcome.
    /// </para>
    /// </remarks>
    /// <param name="source">The aggregate id whose pending reservation is confirmed.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task Confirm(Guid source, CancellationToken cancellationToken = default);

    /// <summary>Cancel the source's pending (unconfirmed) reservation. Returns false if there was none.</summary>
    /// <param name="source">The aggregate id whose pending reservation is dropped.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<bool> RollbackReservation(Guid source, CancellationToken cancellationToken = default);

    /// <summary>Release the source's confirmed name so it can be reused. Returns false if there was none.</summary>
    /// <param name="source">The aggregate id whose confirmed name is released.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<bool> DeleteConfirmedNameReservation(Guid source, CancellationToken cancellationToken = default);
}
