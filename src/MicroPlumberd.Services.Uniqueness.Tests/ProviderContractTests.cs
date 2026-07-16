using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace MicroPlumberd.Services.Uniqueness.Tests;

/// <summary>
/// The per-provider contract. Every scenario here MUST run on each provider — these are exactly the
/// behaviours that differ between databases, so a single-provider test cannot catch them.
/// SQLite runs by default; PostgreSQL and SQL Server are Integration and skip LOUDLY without a server.
/// </summary>
[Collection(DockerCollection.Name)]
public class ProviderContractTests(DockerServersFixture docker)
{
    const string Nip = "7151960049";

    // ================================================================ UQ-PROV-01

    [Fact] // UQ-PROV-01 / SQLite
    public Task Uq_Prov_01_sqlite() => CoreContract(new SqliteUqProvider());

    [RequiresPostgresFact] // UQ-PROV-01 / PostgreSQL
    [Trait("Category", "Integration")]
    public async Task Uq_Prov_01_postgres() => await CoreContract(new PostgresUqProvider(await docker.Postgres()));

    [RequiresSqlServerFact] // UQ-PROV-01 / SqlServer
    [Trait("Category", "Integration")]
    public async Task Uq_Prov_01_sqlserver() => await CoreContract(new SqlServerUqProvider(await docker.SqlServer()));

    // ================================================================ UQ-PROV-02 [Q1 ruling]

    [Fact] // UQ-PROV-02 / SQLite
    public Task Uq_Prov_02_sqlite() => NamesDifferingOnlyInCaseAreDifferent(new SqliteUqProvider());

    [RequiresPostgresFact] // UQ-PROV-02 / PostgreSQL
    [Trait("Category", "Integration")]
    public async Task Uq_Prov_02_postgres() => await NamesDifferingOnlyInCaseAreDifferent(new PostgresUqProvider(await docker.Postgres()));

    [RequiresSqlServerFact] // UQ-PROV-02 / SqlServer — the provider this scenario exists for
    [Trait("Category", "Integration")]
    public async Task Uq_Prov_02_sqlserver() => await NamesDifferingOnlyInCaseAreDifferent(new SqlServerUqProvider(await docker.SqlServer()));

    // ================================================================ UQ-PROV-03 [structural]

    [Fact] // UQ-PROV-03 / SQLite
    public Task Uq_Prov_03_sqlite() => UniqueIndexOnNameExists(new SqliteUqProvider());

    [RequiresPostgresFact] // UQ-PROV-03 / PostgreSQL
    [Trait("Category", "Integration")]
    public async Task Uq_Prov_03_postgres() => await UniqueIndexOnNameExists(new PostgresUqProvider(await docker.Postgres()));

    [RequiresSqlServerFact] // UQ-PROV-03 / SqlServer
    [Trait("Category", "Integration")]
    public async Task Uq_Prov_03_sqlserver() => await UniqueIndexOnNameExists(new SqlServerUqProvider(await docker.SqlServer()));

    // ================================================================

    /// <summary>
    /// UQ-PROV-02 — the library is ALWAYS binary-exact; callers normalise (design §11 Q1, RULED).
    /// "abc" and "ABC" are DIFFERENT names on every provider.
    /// </summary>
    /// <remarks>
    /// This exists because SQL Server's default collation (SQL_Latin1_General_CP1_CI_AS) is
    /// case-INsensitive, so a plain nvarchar unique index would compare case-insensitively there while
    /// SQLite and PostgreSQL compare exactly — the same two values colliding on one provider and not
    /// another. The SqlServer dialect pins COLLATE Latin1_General_100_BIN2 to prevent that; drop the pin
    /// and the SqlServer case here goes RED.
    /// </remarks>
    static async Task NamesDifferingOnlyInCaseAreDifferent(UqProvider provider)
    {
        await using var h = await UqHarness.Create(provider);
        var uq = await h.StartNip();

        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        await uq.Reserve("abc", a);
        await uq.Confirm(a);

        // A different name, so it must NOT conflict — on every provider.
        await uq.Reserve("ABC", b);
        await uq.Confirm(b);

        (await h.Provider.RowsFor(nameof(CompanyNip), "abc")).Single()
            .Should().Match<ResRow>(r => r.SourceId == a && r.IsConfirmed,
                $"{h.Provider.Name} must still hold 'abc' for A");
        (await h.Provider.RowsFor(nameof(CompanyNip), "ABC")).Single()
            .Should().Match<ResRow>(r => r.SourceId == b && r.IsConfirmed,
                $"{h.Provider.Name} compares names exactly, so 'ABC' is a different name from 'abc'");
    }

    /// <summary>
    /// UQ-PROV-03 — the UNIQUE index on Name actually EXISTS. Structural on purpose: a
    /// CREATE UNIQUE INDEX -> CREATE INDEX slip is invisible to every behavioural test, because the
    /// service's re-read still answers correctly for sequential callers. Proven 2026-07-16: downgrading
    /// SQLite's index to non-unique left 27 of 28 tests green and only this assertion caught it.
    /// </summary>
    static async Task UniqueIndexOnNameExists(UqProvider provider)
    {
        await using var h = await UqHarness.Create(provider);
        await h.Start(s =>
        {
            h.Provider.Register<CompanyNip>(s);
            h.Provider.Register<InvoiceNumber>(s);
        });

        (await h.Provider.HasUniqueIndexOnName(nameof(CompanyNip))).Should().BeTrue(
            $"{h.Provider.Name} must create a UNIQUE index on Name — it IS the lock");
        (await h.Provider.HasUniqueIndexOnName(nameof(InvoiceNumber))).Should().BeTrue(
            $"{h.Provider.Name} must create the lock for EVERY category, not just the first");
    }

    /// <summary>UQ-PROV-01 — the core contract, run end to end through the shipped surface.</summary>
    static async Task CoreContract(UqProvider provider)
    {
        await using var h = await UqHarness.Create(provider);
        var host = await h.Start(s =>
        {
            h.Provider.Register<CompanyNip>(s);
            h.Provider.Register<InvoiceNumber>(s);
        });

        var uq = host.Services.GetRequiredService<IUniqueNameReservation<CompanyNip>>();
        var invoices = host.Services.GetRequiredService<IUniqueNameReservation<InvoiceNumber>>();

        // --- UQ-CAT-01: both tables really exist in this database
        (await h.TableNames()).Should().Contain(new[] { nameof(CompanyNip), nameof(InvoiceNumber) });

        // --- UQ-RES-01: reserve + confirm, and a different source is refused, told who holds it
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await uq.Reserve(Nip, a);
        await uq.Confirm(a);

        var conflict = await Assert.ThrowsAsync<UniqueNameConflictException>(() => uq.Reserve(Nip, b));
        conflict.HeldBy.Should().Be(a);

        // --- UQ-CAT-01: categories do not bleed
        await invoices.Reserve(Nip, b);
        await invoices.Confirm(b);
        (await h.Provider.RowsFor(nameof(InvoiceNumber), Nip)).Single().SourceId.Should().Be(b);

        // --- UQ-RES-03: same-source reserve is idempotent and extends the lease
        var c = Guid.NewGuid();
        const string Other = "3333333333";
        await uq.Reserve(Other, c, TimeSpan.FromSeconds(1));
        var first = (await h.Provider.RowsFor(nameof(CompanyNip), Other)).Single().ValidUntil;

        await uq.Reserve(Other, c, TimeSpan.FromMinutes(10));

        var rows = await h.Provider.RowsFor(nameof(CompanyNip), Other);
        rows.Should().ContainSingle();
        rows.Single().ValidUntil.Should().BeAfter(first);

        // --- UQ-LEASE-01: an expired pending reservation is taken over
        var d = Guid.NewGuid();
        var e = Guid.NewGuid();
        const string Dead = "4444444444";
        await uq.Reserve(Dead, d);
        await h.Provider.ExpireLease(nameof(CompanyNip), Dead);

        await uq.Reserve(Dead, e);
        await uq.Confirm(e);

        var taken = await h.Provider.RowsFor(nameof(CompanyNip), Dead);
        taken.Should().ContainSingle();
        taken.Single().Should().Match<ResRow>(r => r.SourceId == e && r.IsConfirmed);
    }
}
