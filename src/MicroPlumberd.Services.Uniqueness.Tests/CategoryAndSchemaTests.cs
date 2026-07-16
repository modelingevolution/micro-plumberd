using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MicroPlumberd.Services.Uniqueness.Tests;

/// <summary>
/// Per-category tables and schema creation. These assert what really landed in the database, read
/// outside EF — EnsureCreated() would have silently skipped the second table, and only an
/// on-disk assertion catches that.
/// </summary>
public class CategoryAndSchemaTests
{
    const string Nip = "7151960049";

    static Task<UqHarness> Sqlite() => UqHarness.Create(new SqliteUqProvider());

    [Fact] // UQ-CAT-01 — each category gets its own table, and they do not collide
    public async Task Uq_Cat_01_each_category_gets_its_own_table_and_they_do_not_collide()
    {
        await using var h = await Sqlite();
        var host = await h.Start(s =>
        {
            h.Provider.Register<CompanyNip>(s);
            h.Provider.Register<InvoiceNumber>(s);   // same database, second table
        });

        // EnsureCreated() is all-or-nothing per database: it would have created the first table and
        // silently skipped this second one, surfacing only as "no such table" at the first reservation.
        (await h.TableNames()).Should().Contain(new[] { nameof(CompanyNip), nameof(InvoiceNumber) });

        var nips = host.Services.GetRequiredService<IUniqueNameReservation<CompanyNip>>();
        var invoices = host.Services.GetRequiredService<IUniqueNameReservation<InvoiceNumber>>();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        await nips.Reserve("X", a);
        await nips.Confirm(a);

        // the same literal name in a different category is a different set — no collision
        await invoices.Reserve("X", b);
        await invoices.Confirm(b);

        (await h.Provider.RowsFor(nameof(CompanyNip), "X")).Single().SourceId.Should().Be(a);
        (await h.Provider.RowsFor(nameof(InvoiceNumber), "X")).Single().SourceId.Should().Be(b);
    }

    [Fact] // UQ-CAT-02 — a category may override its table name
    public async Task Uq_Cat_02_category_may_override_its_table_name()
    {
        await using var h = await Sqlite();
        await h.Start(s => h.Provider.Register<CustomNamed>(s));

        (await h.TableNames()).Should().Contain("my_custom_table");
        (await h.TableNames()).Should().NotContain(nameof(CustomNamed));
    }

    [Fact] // UQ-CAT-03 — confirm is scoped to its category
    public async Task Uq_Cat_03_confirm_is_scoped_to_its_category()
    {
        await using var h = await Sqlite();
        var host = await h.Start(s =>
        {
            h.Provider.Register<CompanyNip>(s);
            h.Provider.Register<InvoiceNumber>(s);
        });

        var nips = host.Services.GetRequiredService<IUniqueNameReservation<CompanyNip>>();
        var invoices = host.Services.GetRequiredService<IUniqueNameReservation<InvoiceNumber>>();
        var a = Guid.NewGuid();

        await nips.Reserve(Nip, a);
        await invoices.Reserve("INV-1", a);   // same source, pending in BOTH categories

        await nips.Confirm(a);

        (await h.Provider.RowsFor(nameof(CompanyNip), Nip)).Single().IsConfirmed.Should().BeTrue();
        (await h.Provider.RowsFor(nameof(InvoiceNumber), "INV-1")).Single().IsConfirmed
            .Should().BeFalse("cross-category bleed is exactly what the per-category table prevents");
    }

    [Fact] // UQ-SCHEMA-01 — schema creation is idempotent
    public async Task Uq_Schema_01_schema_creation_is_idempotent()
    {
        await using var h = await Sqlite();

        var first = await h.Start(s => h.Provider.Register<CompanyNip>(s));
        var uq = first.Services.GetRequiredService<IUniqueNameReservation<CompanyNip>>();
        var a = Guid.NewGuid();
        await uq.Reserve(Nip, a);
        await uq.Confirm(a);
        await first.StopAsync();

        // Start again over the same database — the DDL runs on every start and must tolerate the table.
        var second = await h.Start(s => h.Provider.Register<CompanyNip>(s));

        var rows = await h.Provider.RowsFor(nameof(CompanyNip), Nip);
        rows.Should().ContainSingle("re-running schema creation must not drop or duplicate data");
        rows.Single().Should().Match<ResRow>(r => r.SourceId == a && r.IsConfirmed);

        var uq2 = second.Services.GetRequiredService<IUniqueNameReservation<CompanyNip>>();
        var conflict = await Assert.ThrowsAsync<UniqueNameConflictException>(
            () => uq2.Reserve(Nip, Guid.NewGuid()));
        conflict.HeldBy.Should().Be(a);
    }

    [Fact] // UQ-SCHEMA-02 — a missing dialect fails loudly at start, naming the fix
    public async Task Uq_Schema_02_missing_dialect_fails_loudly_naming_the_fix()
    {
        await using var h = await Sqlite();

        // The provider-neutral core overload registers no IUniquenessDialect on its own.
        var ex = await Record.ExceptionAsync(() => h.Start(s =>
            s.AddUniqueness<CompanyNip>(o => o.UseSqlite(h.Provider.ConnectionString))));

        ex.Should().NotBeNull("a host that cannot create its schema must not start");
        var message = Flatten(ex!);
        message.Should().Contain("Sqlite", "the error must name the provider that has no dialect");
        message.Should().Contain("MicroPlumberd.Services.Uniqueness.Sqlite",
            "the error must name the package that fixes it");
    }

    [Fact] // UQ-SCHEMA-03 — ensureSchema:false issues no DDL
    public async Task Uq_Schema_03_ensure_schema_false_issues_no_ddl()
    {
        await using var h = await Sqlite();

        var host = await h.Start(s => h.Provider.Register<CompanyNip>(s, ensureSchema: false));

        // Opting out must be TOTAL: a host without DDL rights must not attempt DDL.
        (await h.TableNames()).Should().NotContain(nameof(CompanyNip));

        var uq = host.Services.GetRequiredService<IUniqueNameReservation<CompanyNip>>();
        var ex = await Record.ExceptionAsync(() => uq.Reserve(Nip, Guid.NewGuid()));

        ex.Should().NotBeNull("reserving must fail because the table is absent");
        ex.Should().NotBeOfType<UniqueNameConflictException>(
            "a missing table is not a name conflict");
    }

    static string Flatten(Exception ex)
    {
        var sb = new System.Text.StringBuilder();
        for (Exception? e = ex; e is not null; e = e.InnerException) sb.AppendLine(e.Message);
        return sb.ToString();
    }
}
