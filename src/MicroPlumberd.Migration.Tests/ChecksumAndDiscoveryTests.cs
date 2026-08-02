using System.Reflection;
using FluentAssertions;
using MicroPlumberd.Migration;
using Xunit;

namespace MicroPlumberd.Migration.Tests;

public class ChecksumAndDiscoveryTests
{
    [Fact]
    public void Checksum_is_stable_across_recompiles_of_the_same_rules()
    {
        var a = CompiledMigration.Compile(new TestMigrations.DropManyStreams());
        var b = CompiledMigration.Compile(new TestMigrations.DropManyStreams());
        a.Checksum.Should().Be(b.Checksum);
    }

    [Fact]
    public void Checksum_changes_when_literal_arguments_change()
    {
        var a = CompiledMigration.Compile(new InlineMigration("x", b => b.DropStream("Foo-1")));
        var b = CompiledMigration.Compile(new InlineMigration("x", b => b.DropStream("Foo-2")));
        a.Checksum.Should().NotBe(b.Checksum);
    }

    [Fact]
    public void Checksum_changes_when_the_set_of_operations_changes()
    {
        var a = CompiledMigration.Compile(new InlineMigration("x", b => b.DropStream("Foo-1")));
        var b = CompiledMigration.Compile(new InlineMigration("x", b =>
        {
            b.DropStream("Foo-1");
            b.RenameType("A", "B");
        }));
        a.Checksum.Should().NotBe(b.Checksum);
    }

    [Fact]
    public void Checksum_changes_when_a_lambda_body_changes()
    {
        // Two predicates with different bodies must hash differently (IL-sensitive checksum).
        var a = CompiledMigration.Compile(new InlineMigration("x", b => b.DropEvent(e => e.Type == "One")));
        var b = CompiledMigration.Compile(new InlineMigration("x", b => b.DropEvent(e => e.Type == "Two")));
        a.Checksum.Should().NotBe(b.Checksum);
    }

    [Fact]
    public void Discovery_orders_migrations_by_id_and_includes_the_shipped_one()
    {
        var runnerAssembly = typeof(Runner.Migrations._0001_DropTombstonedTestOffers).Assembly;
        var found = MigrationDiscovery.FromAssemblies(runnerAssembly);
        found.Should().ContainSingle(m => m.Id == "0001_drop_tombstoned_test_offers");
        found.Select(m => m.Id).Should().BeInAscendingOrder(StringComparer.Ordinal);
    }

    [Fact]
    public void Discovery_rejects_duplicate_ids()
    {
        var migrations = new Migration[]
        {
            new InlineMigration("dup", _ => { }),
            new InlineMigration("dup", _ => { })
        };
        var act = () => MigrationDiscovery.Order(migrations);
        act.Should().Throw<DuplicateMigrationIdException>().Which.MigrationId.Should().Be("dup");
    }

    private sealed class InlineMigration(string id, Action<IMigrationBuilder> build) : Migration
    {
        public override string Id => id;
        public override void Migrate(IMigrationBuilder b) => build(b);
    }
}
