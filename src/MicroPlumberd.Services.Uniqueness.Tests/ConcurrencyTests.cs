using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace MicroPlumberd.Services.Uniqueness.Tests;

/// <summary>
/// The lock itself. UQ-CONC-01 is the only scenario that proves the unique index is load-bearing, and
/// it MUST run on real PostgreSQL: SQLite is single-writer, so it serialises the race away and would
/// "pass" no matter what the code did.
/// </summary>
[Trait("Category", "Integration")]
[Collection(DockerCollection.Name)]
public class ConcurrencyTests(DockerServersFixture docker)
{
    const string Nip = "7151960049";

    [RequiresPostgresFact] // UQ-CONC-01 — exactly one winner under real contention [R1]
    public async Task Uq_Conc_01_exactly_one_winner_under_real_contention()
    {
        await using var h = await UqHarness.Create(new PostgresUqProvider(await docker.Postgres()));
        var uq = await h.StartNip();

        // A race that passes once proves nothing.
        for (var round = 0; round < 5; round++)
        {
            var name = $"{Nip}-{round}";
            var sources = Enumerable.Range(0, 32).Select(_ => Guid.NewGuid()).ToArray();

            // Release all 32 at the same instant, each on its own connection from the pool.
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var attempts = sources.Select(async src =>
            {
                await gate.Task;
                await Task.Yield();
                try
                {
                    await uq.Reserve(name, src);
                    return (Winner: (Guid?)src, Conflict: (UniqueNameConflictException?)null);
                }
                catch (UniqueNameConflictException ex)
                {
                    return (Winner: null, Conflict: ex);
                }
            }).ToArray();

            gate.SetResult();
            var results = await Task.WhenAll(attempts); // any OTHER exception fails the test, by design

            var winners = results.Where(r => r.Winner is not null).Select(r => r.Winner!.Value).ToArray();
            winners.Should().ContainSingle($"exactly one source may win round {round}");

            var conflicts = results.Where(r => r.Conflict is not null).Select(r => r.Conflict!).ToArray();
            conflicts.Should().HaveCount(31);
            conflicts.Should().OnlyContain(c => c.HeldBy == winners[0],
                "every loser must be told who actually won, not Guid.Empty");

            var rows = await h.Provider.RowsFor(nameof(CompanyNip), name);
            rows.Should().ContainSingle("the unique index must permit exactly one row for the name");
            rows.Single().SourceId.Should().Be(winners[0]);

            await uq.Confirm(winners[0]); // the winner must be able to complete its write
            (await h.Provider.RowsFor(nameof(CompanyNip), name)).Single().IsConfirmed.Should().BeTrue();
        }
    }

    [RequiresPostgresFact] // UQ-CONC-02 — concurrent reserves by the SAME source all succeed [R1, R2]
    public async Task Uq_Conc_02_concurrent_reserves_by_the_same_source_all_succeed()
    {
        await using var h = await UqHarness.Create(new PostgresUqProvider(await docker.Postgres()));
        var uq = await h.StartNip();

        for (var round = 0; round < 5; round++)
        {
            var name = $"{Nip}-same-{round}";
            var a = Guid.NewGuid();

            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var attempts = Enumerable.Range(0, 16).Select(async _ =>
            {
                await gate.Task;
                await Task.Yield();
                // The loser of the INSERT race must re-read, recognise ITSELF, and not report a conflict.
                await uq.Reserve(name, a);
            }).ToArray();

            gate.SetResult();
            await Task.WhenAll(attempts); // a UniqueNameConflictException against itself fails the test

            (await h.Provider.RowsFor(nameof(CompanyNip), name)).Should().ContainSingle();
        }
    }

    // ------------------------------------------------------------------ UQ-CONC-03 [R8]

    [Fact] // UQ-CONC-03 (SQLite) — a non-uniqueness database failure is NOT reported as a conflict
    public Task Uq_Conc_03_non_uniqueness_failure_is_not_a_conflict_sqlite() =>
        AssertNonUniquenessFailurePropagates(new SqliteUqProvider());

    [RequiresPostgresFact] // UQ-CONC-03 (PostgreSQL)
    public async Task Uq_Conc_03_non_uniqueness_failure_is_not_a_conflict_postgres() =>
        await AssertNonUniquenessFailurePropagates(new PostgresUqProvider(await docker.Postgres()));

    /// <summary>
    /// Fault injection, not simulation. The DATABASE raises a real failure on the reserve INSERT — a
    /// BEFORE INSERT trigger reporting PostgreSQL 40P01 deadlock_detected (SQLite: RAISE(ABORT),
    /// SQLITE_CONSTRAINT_TRIGGER). It is not a unique violation (23505 / 2067), and it hits ONLY the
    /// INSERT: the service's SELECTs and DELETEs still work, so the failure is precisely the one under
    /// test. The service runs through its normal shipped registration, untouched.
    ///
    /// A blanket `catch (DbUpdateException)` reads this as "lost the race" and hands back a WRONG
    /// answer — silently denying a free name on an infrastructure blip. This test must fail if the
    /// catch is widened.
    /// </summary>
    static async Task AssertNonUniquenessFailurePropagates(UqProvider provider)
    {
        await using var h = await UqHarness.Create(provider);
        var uq = await h.StartNip();

        await h.Provider.InjectInsertFault(nameof(CompanyNip));

        var a = Guid.NewGuid();
        var thrown = await Record.ExceptionAsync(() => uq.Reserve(Nip, a));

        thrown.Should().NotBeNull("an infrastructure failure must propagate, not be swallowed");
        Flatten(thrown!).Should().Contain(UqProvider.FaultMarker,
            "the failure observed must be the fault that was injected, not some unrelated breakage");
        thrown.Should().NotBeOfType<UniqueNameConflictException>(
            "a deadlock is NOT someone else holding the name — reporting it as a conflict is a wrong " +
            "answer, and would silently deny a free name on an infrastructure blip (R8)");

        // ...and it must certainly not look like a successful reservation.
        (await h.Provider.RowsFor(nameof(CompanyNip), Nip)).Should().BeEmpty();

        // The same applies to the non-throwing overload: it may not quietly answer "taken".
        var tryThrown = await Record.ExceptionAsync(() => uq.TryReserve(Nip, a));
        tryThrown.Should().NotBeNull(
            "TryReserve returning false here would report an infrastructure failure as 'a different " +
            "source holds the name', which is false");
        tryThrown.Should().NotBeOfType<UniqueNameConflictException>();
    }

    static string Flatten(Exception ex)
    {
        var sb = new System.Text.StringBuilder();
        for (Exception? e = ex; e is not null; e = e.InnerException) sb.AppendLine(e.Message);
        return sb.ToString();
    }
}
