using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace MicroPlumberd.Services.Uniqueness.Tests;

/// <summary>
/// UQ-ATOM-01..03 — the interleaving hazards: a write that is interrupted, or a row that changes hands
/// mid-operation, must not corrupt unrelated state or leak a provider exception.
///
/// All three are made deterministic by DATABASE-level fault injection rather than by racing threads or
/// sleeping: the failure lands at an exact point every run. No crash is needed to test atomicity — an
/// injected failure between two writes exercises the same property a crash would.
/// </summary>
public class AtomicityTests
{
    const string Nip = "7151960049";

    static Task<UqHarness> Sqlite() => UqHarness.Create(new SqliteUqProvider());

    static SqliteUqProvider Faults(UqHarness h) => (SqliteUqProvider)h.Provider;

    [Fact] // UQ-ATOM-01 — Confirm must not release the old name unless the new one is confirmed [Finding 2]
    public async Task Uq_Atom_01_confirm_must_not_release_the_old_name_unless_the_new_one_is_confirmed()
    {
        await using var h = await Sqlite();
        var uq = await h.StartNip();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        await uq.Reserve("1111111111", a);
        await uq.Confirm(a);                     // A owns 1111111111
        await uq.Reserve("2222222222", a);       // rename in flight, not yet confirmed

        // Fail ONLY the write that flips IsConfirmed; the ExecuteDelete releasing the old name still runs.
        await Faults(h).InjectUpdateFault(nameof(CompanyNip));

        await Assert.ThrowsAnyAsync<Exception>(() => uq.Confirm(a));

        // Without a transaction the old name is gone, the new one was never confirmed, and A holds
        // NOTHING — while its aggregate is already persisted claiming the new name.
        (await h.Provider.RowsFor(nameof(CompanyNip), "1111111111")).Should().ContainSingle(
            "an interrupted Confirm must not release the source's previous name")
            .Which.Should().Match<ResRow>(r => r.SourceId == a && r.IsConfirmed);

        // ...and it must still be A's, not free for the taking.
        var conflict = await Assert.ThrowsAsync<UniqueNameConflictException>(
            () => uq.Reserve("1111111111", b));
        conflict.HeldBy.Should().Be(a);
    }

    [Fact] // UQ-ATOM-02 — a FAILED TryReserve must not destroy the source's other pending reservation [Finding 3]
    public async Task Uq_Atom_02_failed_tryreserve_must_not_destroy_the_sources_other_pending_reservation()
    {
        await using var h = await Sqlite();
        var uq = await h.StartNip();
        var a = Guid.NewGuid();

        await uq.Reserve("AAAAAAAAAA", a);       // pending, unrelated to what follows

        // "CCCCCCCCCC" is FREE at SELECT time and the INSERT then fails — i.e. a LOST RACE. This is the
        // only shape that reaches the supersede delete: a name already held returns early at
        // `existing.SourceId != source` and never gets there, so the obvious test proves nothing.
        await h.Provider.InjectInsertFault(nameof(CompanyNip));

        bool? succeeded = null;
        var thrown = await Record.ExceptionAsync(async () => succeeded = await uq.TryReserve("CCCCCCCCCC", a));
        (thrown is not null || succeeded == false).Should().BeTrue(
            "the call must not succeed: the INSERT failed");

        // The destructive supersede delete must not survive a call that achieved nothing.
        (await h.Provider.RowsFor(nameof(CompanyNip), "AAAAAAAAAA")).Should().ContainSingle(
            "a failed reserve must not destroy the source's unrelated pending reservation")
            .Which.SourceId.Should().Be(a);
    }

    [Fact] // UQ-ATOM-03 — a failed lease extension must not leak an infrastructure exception [Finding 1]
    public async Task Uq_Atom_03_failed_lease_extension_must_not_leak_an_infrastructure_exception()
    {
        await using var h = await Sqlite();
        var uq = await h.StartNip();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        await uq.Reserve(Nip, a);                // A holds it, pending

        // A's row is swept and the name retaken by B exactly as A's extension UPDATE lands.
        await Faults(h).SwapOwnerOnUpdate(nameof(CompanyNip), Nip, b);

        bool? result = null;
        var thrown = await Record.ExceptionAsync(async () => result = await uq.TryReserve(Nip, a));

        // TryReserve's contract is to RETURN A BOOL. A read-modify-write extension would affect 0 rows and
        // throw DbUpdateConcurrencyException (a DbUpdateException) straight out of it — a provider error
        // leaking to the caller (R8). A swept row is ordinary contention, not an exception.
        thrown.Should().BeNull(
            "TryReserve must answer with a bool, not leak a provider exception such as " +
            $"{nameof(DbUpdateConcurrencyException)}; got {thrown?.GetType().Name}: {thrown?.Message}");

        // ...and it must answer about the world: B holds the name now.
        result.Should().BeFalse("the name is now held by B, so the answer is a plain 'no'");

        var conflict = await Assert.ThrowsAsync<UniqueNameConflictException>(() => uq.Reserve(Nip, a));
        conflict.HeldBy.Should().Be(b, "the conflict must name the real holder, never Guid.Empty");
    }
}
