using FluentAssertions;

namespace MicroPlumberd.Services.Uniqueness.Tests;

/// <summary>
/// Reserve / confirm / cancel / rename / lease behaviour of the shipped service, on SQLite.
/// Provider-independent logic lives here; the lock itself is proven on real PostgreSQL in
/// <see cref="ConcurrencyTests"/>, which SQLite's single-writer locking cannot do.
/// </summary>
public class ReservationTests
{
    const string Nip = "7151960049";

    static Task<UqHarness> Sqlite() => UqHarness.Create(new SqliteUqProvider());

    /// <summary>Kills a lease by backdating it in the database. Deterministic: no sleeping, and no
    /// timing race that could make these tests flaky.</summary>
    static Task Kill(UqHarness h, string name) => h.Provider.ExpireLease(nameof(CompanyNip), name);

    // ---------------------------------------------------------------- Reserve / Confirm

    [Fact] // UQ-RES-01 — a free name can be reserved and confirmed
    public async Task Uq_Res_01_free_name_can_be_reserved_and_confirmed()
    {
        await using var h = await Sqlite();
        var uq = await h.StartNip();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        await uq.Reserve(Nip, a);
        await uq.Confirm(a);

        var conflict = await Assert.ThrowsAsync<UniqueNameConflictException>(() => uq.Reserve(Nip, b));
        conflict.HeldBy.Should().Be(a, "the loser must be told who actually holds the name");
        conflict.Name.Should().Be(Nip);
    }

    [Fact] // UQ-RES-02 — a pending (unconfirmed, unexpired) reservation already blocks others [R3]
    public async Task Uq_Res_02_pending_reservation_blocks_other_sources()
    {
        await using var h = await Sqlite();
        var uq = await h.StartNip();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        await uq.Reserve(Nip, a, TimeSpan.FromMinutes(10)); // A has NOT confirmed

        var conflict = await Assert.ThrowsAsync<UniqueNameConflictException>(() => uq.Reserve(Nip, b));
        conflict.HeldBy.Should().Be(a);
    }

    [Fact] // UQ-RES-03 — reserving is idempotent for the same source, and extends the lease [R2]
    public async Task Uq_Res_03_reserve_is_idempotent_for_the_same_source_and_extends_the_lease()
    {
        await using var h = await Sqlite();
        var uq = await h.StartNip();
        var a = Guid.NewGuid();

        await uq.Reserve(Nip, a, TimeSpan.FromSeconds(1));
        var first = (await h.Provider.RowsFor(nameof(CompanyNip), Nip)).Single().ValidUntil;

        await uq.Reserve(Nip, a, TimeSpan.FromMinutes(10)); // the ERP bug: a retry must not 409 against itself

        var rows = await h.Provider.RowsFor(nameof(CompanyNip), Nip);
        rows.Should().ContainSingle("a retry must not create a second row");
        rows.Single().ValidUntil.Should().BeAfter(first, "the lease must be extended on re-reserve");
    }

    [Fact] // UQ-RES-04 — TryReserve reports rather than throws
    public async Task Uq_Res_04_try_reserve_reports_rather_than_throws()
    {
        await using var h = await Sqlite();
        var uq = await h.StartNip();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        await uq.Reserve(Nip, a);
        await uq.Confirm(a);

        (await uq.TryReserve(Nip, b)).Should().BeFalse();
    }

    // ---------------------------------------------------------------- The lease [R4]

    [Fact] // UQ-LEASE-01 — an expired pending reservation is taken over
    public async Task Uq_Lease_01_expired_pending_reservation_is_taken_over()
    {
        await using var h = await Sqlite();
        var uq = await h.StartNip();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        await uq.Reserve(Nip, a);   // A crashed between reserve and persist; never confirmed
        await Kill(h, Nip);

        await uq.Reserve(Nip, b); // must NOT throw — a hand-rolled index burns the name forever here
        await uq.Confirm(b);

        var rows = await h.Provider.RowsFor(nameof(CompanyNip), Nip);
        rows.Should().ContainSingle();
        rows.Single().SourceId.Should().Be(b);
        rows.Single().IsConfirmed.Should().BeTrue();
    }

    [Fact] // UQ-LEASE-02 — a confirmed reservation never expires
    public async Task Uq_Lease_02_confirmed_reservation_never_expires()
    {
        await using var h = await Sqlite();
        var uq = await h.StartNip();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        await uq.Reserve(Nip, a);
        await uq.Confirm(a);
        await Kill(h, Nip);   // ValidUntil is now in the past, but confirmed names are permanent

        var conflict = await Assert.ThrowsAsync<UniqueNameConflictException>(() => uq.Reserve(Nip, b));
        conflict.HeldBy.Should().Be(a, "ValidUntil must be ignored once confirmed");
    }

    [Fact] // UQ-LEASE-03 — confirming an expired lease is refused [R6]
    public async Task Uq_Lease_03_confirming_an_expired_lease_is_refused()
    {
        await using var h = await Sqlite();
        var uq = await h.StartNip();
        var a = Guid.NewGuid();

        await uq.Reserve(Nip, a);
        await Kill(h, Nip);

        // The name may already belong to someone else; confirming blind would double-book.
        await Assert.ThrowsAnyAsync<Exception>(() => uq.Confirm(a));

        var rows = await h.Provider.RowsFor(nameof(CompanyNip), Nip);
        rows.Where(x => x.IsConfirmed).Should().BeEmpty("a refused Confirm must not have confirmed anything");
    }

    // ---------------------------------------------------------------- Cancel / release

    [Fact] // UQ-CANCEL-01 — rollback frees the name
    public async Task Uq_Cancel_01_rollback_frees_the_name()
    {
        await using var h = await Sqlite();
        var uq = await h.StartNip();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        await uq.Reserve(Nip, a);

        (await uq.RollbackReservation(a)).Should().BeTrue();

        await uq.Reserve(Nip, b);
        await uq.Confirm(b);
        (await h.Provider.RowsFor(nameof(CompanyNip), Nip)).Single().SourceId.Should().Be(b);
    }

    [Fact] // UQ-CANCEL-02 — rollback does not touch a confirmed reservation
    public async Task Uq_Cancel_02_rollback_does_not_touch_a_confirmed_reservation()
    {
        await using var h = await Sqlite();
        var uq = await h.StartNip();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        await uq.Reserve(Nip, a);
        await uq.Confirm(a);

        (await uq.RollbackReservation(a)).Should().BeFalse();

        var conflict = await Assert.ThrowsAsync<UniqueNameConflictException>(() => uq.Reserve(Nip, b));
        conflict.HeldBy.Should().Be(a);
    }

    [Fact] // UQ-CANCEL-03 — releasing a confirmed name frees it
    public async Task Uq_Cancel_03_releasing_a_confirmed_name_frees_it()
    {
        await using var h = await Sqlite();
        var uq = await h.StartNip();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        await uq.Reserve(Nip, a);
        await uq.Confirm(a);

        (await uq.DeleteConfirmedNameReservation(a)).Should().BeTrue();

        await uq.Reserve(Nip, b);
        await uq.Confirm(b);
        (await h.Provider.RowsFor(nameof(CompanyNip), Nip)).Single().SourceId.Should().Be(b);
    }

    // ---------------------------------------------------------------- Rename [R5, R6]

    [Fact] // UQ-RENAME-01 — confirming a new name releases the source's previous one
    public async Task Uq_Rename_01_confirming_a_new_name_releases_the_previous_one()
    {
        await using var h = await Sqlite();
        var uq = await h.StartNip();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        await uq.Reserve("1111111111", a);
        await uq.Confirm(a);

        await uq.Reserve("2222222222", a);
        await uq.Confirm(a);

        (await h.Provider.RowsFor(nameof(CompanyNip), "2222222222")).Single()
            .Should().Match<ResRow>(r => r.SourceId == a && r.IsConfirmed);

        // the old name must be free for someone else
        await uq.Reserve("1111111111", b);
        await uq.Confirm(b);
    }

    [Fact] // UQ-RENAME-02 — a second pending reservation supersedes the first [R5]
    public async Task Uq_Rename_02_second_pending_reservation_supersedes_the_first()
    {
        await using var h = await Sqlite();
        var uq = await h.StartNip();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        await uq.Reserve("2222222222", a);   // pending, never confirmed
        await uq.Reserve("3333333333", a);   // supersedes it — two pending rows would make Confirm ambiguous

        await uq.Confirm(a);                 // must not throw

        (await h.Provider.RowsFor(nameof(CompanyNip), "3333333333")).Single()
            .Should().Match<ResRow>(r => r.SourceId == a && r.IsConfirmed);
        (await h.Provider.RowsFor(nameof(CompanyNip), "2222222222")).Should().BeEmpty();

        // and it really is free
        await uq.Reserve("2222222222", b);
        await uq.Confirm(b);
    }

    [Fact] // UQ-CONFIRM-01 — confirm is idempotent
    public async Task Uq_Confirm_01_confirm_is_idempotent()
    {
        await using var h = await Sqlite();
        var uq = await h.StartNip();
        var a = Guid.NewGuid();

        await uq.Reserve(Nip, a);
        await uq.Confirm(a);
        await uq.Confirm(a); // must not throw

        var rows = await h.Provider.RowsFor(nameof(CompanyNip), Nip);
        rows.Should().ContainSingle();
        rows.Single().Should().Match<ResRow>(r => r.SourceId == a && r.IsConfirmed);
    }

    [Fact] // UQ-CONFIRM-02 — confirming with nothing pending is an error
    public async Task Uq_Confirm_02_confirming_with_nothing_pending_is_an_error()
    {
        await using var h = await Sqlite();
        var uq = await h.StartNip();

        await Assert.ThrowsAnyAsync<Exception>(() => uq.Confirm(Guid.NewGuid()));
    }

    // ---------------------------------------------------------------- Overtake / compensation [R9]

    [Fact] // UQ-FENCE-01 — an overtaken source is told, and MUST compensate
    public async Task Uq_Fence_01_overtaken_source_confirm_throws()
    {
        await using var h = await Sqlite();
        var uq = await h.StartNip();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        await uq.Reserve(Nip, a);   // A stalls past its lease
        await Kill(h, Nip);

        await uq.Reserve(Nip, b);              // B collects A's dead row and takes the name
        await uq.Confirm(b);

        // A may already have persisted its aggregate. Confirm MUST fail loudly so the caller
        // compensates — a silent success here means two aggregates hold one unique value. R9.
        await Assert.ThrowsAnyAsync<Exception>(() => uq.Confirm(a));

        var rows = await h.Provider.RowsFor(nameof(CompanyNip), Nip);
        rows.Should().ContainSingle();
        rows.Single().SourceId.Should().Be(b, "B must still hold the name after A's failed Confirm");
    }

    [Fact] // UQ-FENCE-02 — an overtaken source cannot silently re-take the name
    public async Task Uq_Fence_02_overtaken_source_cannot_silently_retake_the_name()
    {
        await using var h = await Sqlite();
        var uq = await h.StartNip();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        await uq.Reserve(Nip, a);
        await Kill(h, Nip);

        await uq.Reserve(Nip, b);
        await uq.Confirm(b);

        // A retrying after being overtaken must NOT look like idempotent success.
        var conflict = await Assert.ThrowsAsync<UniqueNameConflictException>(() => uq.Reserve(Nip, a));
        conflict.HeldBy.Should().Be(b);
    }
}
