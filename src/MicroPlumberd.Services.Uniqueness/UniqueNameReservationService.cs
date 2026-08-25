using System.Runtime.ExceptionServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MicroPlumberd.Services.Uniqueness;

/// <summary>
/// Reservation-pattern uniqueness. Correctness rests on the unique index on Name: concurrent writers
/// race to INSERT, exactly one wins, and the loser re-reads to find out who holds the name. Nothing
/// here trusts a read-then-write gap.
/// </summary>
/// <remarks>
/// Every lease is measured on the DATABASE clock, never the app server's, so that instances with skewed
/// clocks cannot shorten or lengthen each other's leases (design R10).
/// </remarks>
class UniqueNameReservationService<TCategory>(
    IDbContextFactory<UniquenessDb<TCategory>> factory,
    IEnumerable<IUniquenessDialect> dialects,
    ILogger<UniqueNameReservationService<TCategory>> logger)
    : IUniqueNameReservation<TCategory>
{
    static readonly TimeSpan DefaultLease = TimeSpan.FromMinutes(10);
    static string Category => CategoryName<TCategory>.Value;

    IUniquenessDialect? _dialect;

    public async Task Reserve(string name, Guid source, TimeSpan? duration = null, CancellationToken cancellationToken = default)
    {
        if (await TryReserve(name, source, duration, cancellationToken)) return;

        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var holder = await db.Reservations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Name == name, cancellationToken);
        throw new UniqueNameConflictException(name, Category, holder?.SourceId ?? Guid.Empty);
    }

    /// <summary>How many times to re-evaluate when the row we were acting on vanishes under us.</summary>
    /// <remarks>Each retry follows a real state change made by someone else, not a backoff — a caller that
    /// loses three times running is contending with a live rename storm and gets an honest "no".</remarks>
    const int MaxAttempts = 3;

    public async Task<bool> TryReserve(string name, Guid source, TimeSpan? duration = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        for (var attempt = 1; ; attempt++)
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            var now = await DbUtcNow(db, cancellationToken);
            var validUntil = now.Add(duration ?? DefaultLease);

            // A dead lease (owner crashed between reserve and confirm) must not hold the name forever.
            // Deliberately outside the transaction below: it is idempotent GC, and holding its locks for
            // the duration of the insert would only add contention.
            await db.Reservations
                .Where(x => x.Name == name && !x.IsConfirmed && x.ValidUntil < now)
                .ExecuteDeleteAsync(cancellationToken);

            var existing = await db.Reservations.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Name == name, cancellationToken);

            if (existing is not null)
            {
                if (existing.SourceId != source) return false;      // held by someone else
                if (existing.IsConfirmed) return true;              // ours already, permanently

                // Ours and still pending -> extend the lease. Guarded: a read-modify-write here would be a
                // lost update. Between the read above and this write our lease can expire and be swept by
                // someone else, whose SaveChanges then affects 0 rows and throws DbUpdateConcurrencyException
                // — leaking a provider exception out of a bool-returning method (R8). ExecuteUpdate states
                // the whole condition in the UPDATE itself and reports rows affected instead of throwing.
                var extended = await db.Reservations
                    .Where(x => x.Name == name && x.SourceId == source && !x.IsConfirmed)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.ValidUntil, validUntil), cancellationToken);

                if (extended > 0) return true;                      // idempotent for the same source

                // Our row went away mid-flight: the lease expired and was swept. The name may now be free
                // or taken; either way our previous read is stale, so re-evaluate rather than guess.
                if (attempt >= MaxAttempts) return false;
                continue;
            }

            // The name looks free. The supersede-delete and the insert MUST be atomic: the delete is
            // destructive to unrelated state (this source's other pending reservation), and the insert can
            // still lose the race on the unique index. Committed separately, a failed insert would leave the
            // source's other reservation destroyed by a call that returned false and changed nothing else.
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

            // At most one pending reservation per source, so Confirm stays unambiguous (supersede a rename).
            await db.Reservations
                .Where(x => x.SourceId == source && !x.IsConfirmed && x.Name != name)
                .ExecuteDeleteAsync(cancellationToken);

            db.Reservations.Add(new UniqueNameReservation
            {
                Name = name, SourceId = source, ValidUntil = validUntil, IsConfirmed = false
            });

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateException ex)
            {
                // Rolls the supersede-delete back with it, so a lost race costs us nothing we held.
                await tx.RollbackAsync(cancellationToken);
                return await ProveOutcome(name, source, ex, cancellationToken);
            }
        }
    }

    /// <summary>
    /// The INSERT failed. Decide what actually happened by PROVING the resulting state, never by assuming
    /// the failure was a unique violation (R8).
    /// </summary>
    /// <remarks>
    /// A blanket "lost the race" here would read a deadlock, a timeout or a dropped connection as a
    /// conflict and hand back a confident, wrong answer. So: re-read on a clean context and answer about
    /// the world, not about the error code.
    /// <list type="bullet">
    /// <item>a row for the name owned by us — our insert (or our own concurrent one) landed: true.</item>
    /// <item>a row owned by someone else — we do not hold the name, whatever the driver said: false.</item>
    /// <item>NO row — nothing claimed the name, so this was never a unique violation: rethrow.</item>
    /// </list>
    /// </remarks>
    async Task<bool> ProveOutcome(string name, Guid source, DbUpdateException failure, CancellationToken cancellationToken)
    {
        UniqueNameReservation? holder;
        try
        {
            await using var check = await factory.CreateDbContextAsync(cancellationToken);
            holder = await check.Reservations.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Name == name, cancellationToken);
        }
        catch (Exception probeFailure) when (probeFailure is not OperationCanceledException)
        {
            // Could not establish anything. The original failure stands, unclassified.
            logger.LogError(probeFailure,
                "Uniqueness[{Category}]: could not re-read '{Name}' after a failed reserve INSERT for source {Source}; " +
                "rethrowing the original failure.", Category, name, source);
            ExceptionDispatchInfo.Capture(failure).Throw();
            throw; // unreachable; keeps the compiler happy
        }

        if (holder is null)
        {
            logger.LogError(failure,
                "Uniqueness[{Category}]: reserving '{Name}' for source {Source} failed and no row holds the name — " +
                "this is not a unique-constraint violation, so it is not a conflict.", Category, name, source);
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        if (holder!.SourceId == source)
        {
            logger.LogDebug("Uniqueness[{Category}]: '{Name}' is already held by the same source {Source}.",
                Category, name, source);
            return true;
        }

        logger.LogDebug("Uniqueness[{Category}]: source {Source} lost the race for '{Name}' to {Holder}.",
            Category, source, name, holder.SourceId);
        return false;
    }

    public async Task Confirm(Guid source, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);

        // Read the clock before opening the transaction: on PostgreSQL now() would be the TRANSACTION's
        // start time, not the current instant. (The dialect uses clock_timestamp() for exactly this reason,
        // but not depending on that here keeps the two facts independent.)
        var now = await DbUtcNow(db, cancellationToken);

        // Releasing the old name and confirming the new one MUST be atomic. Committed separately, a failure
        // between them releases the source's old name while the new one is still only pending — the pending
        // row then expires and the source holds NOTHING, while its aggregate is already persisted claiming
        // the new name. R9 does not cover this: R9 is about Confirm THROWING, and this is Confirm being
        // INTERRUPTED, so no compensation ever runs. Inside a transaction the same failure leaves the old
        // name confirmed and the new one pending — a coherent state the caller can simply retry.
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        var pending = await db.Reservations
            .FirstOrDefaultAsync(x => x.SourceId == source && !x.IsConfirmed, cancellationToken);
        if (pending is null)
        {
            if (await db.Reservations.AnyAsync(x => x.SourceId == source && x.IsConfirmed, cancellationToken))
                return;                                             // already confirmed -> idempotent
            throw new InvalidOperationException(
                $"No pending reservation to confirm for source {source} in category '{Category}'. " +
                "If an aggregate was persisted against it, that aggregate MUST be compensated.");
        }

        if (pending.ValidUntil <= now)
            throw new InvalidOperationException(
                $"The reservation of '{pending.Name}' for source {source} in category '{Category}' expired " +
                "before it was confirmed; the name may already belong to someone else. If an aggregate was " +
                "persisted against it, that aggregate MUST be compensated.");

        // Renaming: the source's previously confirmed name is released as the new one is confirmed.
        await db.Reservations
            .Where(x => x.SourceId == source && x.IsConfirmed)
            .ExecuteDeleteAsync(cancellationToken);

        pending.IsConfirmed = true;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // The pending row was swept between the read and the update: this source was overtaken. Say so
            // in the library's own terms rather than leaking EF's, and state the obligation (R9).
            throw new InvalidOperationException(
                $"The reservation of '{pending.Name}' for source {source} in category '{Category}' was " +
                "taken over before it could be confirmed; the name may already belong to someone else. If " +
                "an aggregate was persisted against it, that aggregate MUST be compensated.", ex);
        }

        await tx.CommitAsync(cancellationToken);
    }

    public async Task<bool> RollbackReservation(Guid source, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.Reservations.Where(x => x.SourceId == source && !x.IsConfirmed)
            .ExecuteDeleteAsync(cancellationToken) > 0;
    }

    public async Task<bool> DeleteConfirmedNameReservation(Guid source, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.Reservations.Where(x => x.SourceId == source && x.IsConfirmed)
            .ExecuteDeleteAsync(cancellationToken) > 0;
    }

    /// <summary>
    /// The current UTC instant according to the DATABASE — the one clock every instance shares (R10).
    /// </summary>
    /// <remarks>
    /// Costs one round-trip per operation. The value is marginally stale by that round-trip, which errs in
    /// the safe direction on both uses: an expired row is collected slightly late (never early), and a new
    /// lease runs slightly short (never long).
    /// </remarks>
    async Task<DateTime> DbUtcNow(UniquenessDb<TCategory> db, CancellationToken cancellationToken)
    {
        var sql = $"SELECT {Dialect(db).UtcNowSql} AS \"Value\"";
        var now = await db.Database.SqlQueryRaw<DateTime>(sql).SingleAsync(cancellationToken);
        // The dialect contract is that the expression yields UTC; providers differ on whether they label it
        // (Npgsql timestamptz says Utc, SQLite TEXT and SqlServer datetime2 say Unspecified). Force it, so
        // the Kind can never silently shift a lease by the local offset.
        return DateTime.SpecifyKind(now, DateTimeKind.Utc);
    }

    IUniquenessDialect Dialect(UniquenessDb<TCategory> db)
    {
        // Benign race on first use: resolution is deterministic and idempotent.
        return _dialect ??= Resolve(db.Database.ProviderName ?? "");

        IUniquenessDialect Resolve(string provider) =>
            dialects.FirstOrDefault(d => d.Handles(provider))
            ?? throw new InvalidOperationException(
                $"No {nameof(IUniquenessDialect)} is registered for EF provider '{provider}', so leases cannot " +
                "be measured on the database clock. Install MicroPlumberd.Services.Uniqueness.Sqlite, .Postgres " +
                "or .SqlServer (and use its AddUniquenessXxx overload), or register your own dialect.");
    }
}
