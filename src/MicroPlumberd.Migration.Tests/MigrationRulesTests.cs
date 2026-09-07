using System.Text.Json.Nodes;
using FluentAssertions;
using MicroPlumberd.Migration;
using Xunit;

namespace MicroPlumberd.Migration.Tests;

/// <summary>Unit tests for the raw rule engine — no EventStore required.</summary>
public class MigrationRulesTests
{
    private static EventContext Ctx(string stream, string type, string dataJson = "{}", string? metaJson = null) =>
        new()
        {
            SourceStream = stream,
            EventNumber = 0,
            TargetStream = stream,
            Type = type,
            Data = JsonNode.Parse(dataJson),
            Meta = metaJson is null ? new JsonObject() : JsonNode.Parse(metaJson)
        };

    private static MigrationPlan Plan(params Migration[] migrations) =>
        new(migrations.Select(CompiledMigration.Compile).ToList());

    private static EventContext Fold(EventContext ctx, params Migration[] migrations)
    {
        Plan(migrations).Apply(ctx, null);
        return ctx;
    }

    [Fact]
    public void DropStream_drops_matching_stream_only()
    {
        Fold(Ctx("Foo-1", "X"), new TestMigrations.DropOneStream()).Dropped.Should().BeTrue();
        Fold(Ctx("Foo-2", "X"), new TestMigrations.DropOneStream()).Dropped.Should().BeFalse();
    }

    [Fact]
    public void DropStreams_drops_every_listed_stream()
    {
        Fold(Ctx("Foo-1", "X"), new TestMigrations.DropManyStreams()).Dropped.Should().BeTrue();
        Fold(Ctx("Foo-2", "X"), new TestMigrations.DropManyStreams()).Dropped.Should().BeTrue();
        Fold(Ctx("Foo-3", "X"), new TestMigrations.DropManyStreams()).Dropped.Should().BeFalse();
    }

    [Fact]
    public void DropStream_predicate_drops_by_name_shape()
    {
        Fold(Ctx("Temp-abc", "X"), new TestMigrations.DropByStreamPredicate()).Dropped.Should().BeTrue();
        Fold(Ctx("Keep-abc", "X"), new TestMigrations.DropByStreamPredicate()).Dropped.Should().BeFalse();
    }

    [Fact]
    public void DropEvent_predicate_drops_by_event_view()
    {
        Fold(Ctx("Foo-1", "Noise"), new TestMigrations.DropByEventPredicate()).Dropped.Should().BeTrue();
        Fold(Ctx("Foo-1", "Signal"), new TestMigrations.DropByEventPredicate()).Dropped.Should().BeFalse();
    }

    [Fact]
    public void RenameType_rewrites_the_event_type_string()
    {
        var ctx = Fold(Ctx("Foo-1", "OldName"), new TestMigrations.RenameTheType());
        ctx.Dropped.Should().BeFalse();
        ctx.Type.Should().Be("NewName");
    }

    [Fact]
    public void TransformJson_mutates_payload_and_metadata_in_place()
    {
        var ctx = Fold(Ctx("Foo-1", "Widget", "{\"a\":1}"), new TestMigrations.TransformData());
        ctx.Data!["a"]!.GetValue<int>().Should().Be(1);
        ctx.Data!["added"]!.GetValue<string>().Should().Be("yes");
        ctx.Meta!["touched"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void TransformJson_does_not_touch_other_types()
    {
        var ctx = Fold(Ctx("Foo-1", "Gadget", "{\"a\":1}"), new TestMigrations.TransformData());
        (ctx.Data as JsonObject)!.ContainsKey("added").Should().BeFalse();
    }

    [Fact]
    public void RenameStream_retargets_events_to_the_new_stream()
    {
        var ctx = Fold(Ctx("Old-1", "X"), new TestMigrations.RenameTheStream());
        ctx.TargetStream.Should().Be("New-1");
    }

    [Fact]
    public void RenameType_then_TransformJson_composes_in_declared_order()
    {
        var ctx = Fold(Ctx("Foo-1", "V1", "{}"), new TestMigrations.RenameThenTransform());
        ctx.Type.Should().Be("V2");
        ctx.Data!["v2"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void RenameStream_then_DropStream_composes_in_declared_order()
    {
        // Renamed to B-1, then the drop that targets B-1 fires.
        Fold(Ctx("A-1", "X"), new TestMigrations.RenameStreamThenDrop()).Dropped.Should().BeTrue();
    }

    [Fact]
    public void Dropped_event_is_not_transformed_afterwards()
    {
        // Drop by stream first, then a transform that would otherwise apply — must be skipped.
        var drop = new TestMigrations.DropOneStream();       // drops Foo-1
        var transform = new InlineMigration("0099_t", b =>
            b.TransformJson("Widget", (d, _) => d["should"] = "not-run"));
        var ctx = Fold(Ctx("Foo-1", "Widget", "{}"), drop, transform);
        ctx.Dropped.Should().BeTrue();
        (ctx.Data as JsonObject)!.ContainsKey("should").Should().BeFalse();
    }

    [Fact]
    public void Stats_attribute_effects_to_the_owning_migration()
    {
        var plan = Plan(new TestMigrations.RenameTheType(), new TestMigrations.DropByEventPredicate());

        plan.Apply(Ctx("Foo-1", "OldName"), null);   // rename fires
        plan.Apply(Ctx("Foo-1", "Noise"), null);     // drop fires
        plan.Apply(Ctx("Foo-1", "OldName"), null);   // rename fires again

        plan.Stats["0005_rename_type"].Renamed.Should().Be(2);
        plan.Stats["0004_drop_event"].Dropped.Should().Be(1);
    }

    [Fact]
    public void TransformJson_is_skipped_for_non_json_payload()
    {
        var ctx = Ctx("Foo-1", "Widget");
        ctx.Data = null; // simulate a non-JSON (binary) payload
        Fold(ctx, new TestMigrations.TransformData());
        ctx.Data.Should().BeNull();          // untouched
        ctx.Dropped.Should().BeFalse();      // did not crash
    }

    [Fact]
    public void Plan_requires_payload_parse_only_when_a_rule_needs_it()
    {
        // DropStreams-only: the payload is never inspected → parse NOTHING (byte-verbatim copy).
        var drops = Plan(new TestMigrations.DropManyStreams());
        drops.RequiresPayloadParse("Anything").Should().BeFalse();
        drops.TransformTypes.Should().BeEmpty();
        drops.AnyDropEventRule.Should().BeFalse();

        // TransformJson is per-TYPE → only that type needs parsing.
        var transform = Plan(new TestMigrations.TransformData()); // TransformJson("Widget", …)
        transform.TransformTypes.Should().Contain("Widget");
        transform.RequiresPayloadParse("Widget").Should().BeTrue();
        transform.RequiresPayloadParse("Other").Should().BeFalse();

        // DropEvent hands the predicate PARSED JSON and is opaque → conservatively parse EVERY event.
        var dropEvent = Plan(new TestMigrations.DropByEventPredicate());
        dropEvent.AnyDropEventRule.Should().BeTrue();
        dropEvent.RequiresPayloadParse("Anything").Should().BeTrue();
    }

    private sealed class InlineMigration(string id, Action<IMigrationBuilder> build) : Migration
    {
        public override string Id => id;
        public override void Migrate(IMigrationBuilder b) => build(b);
    }
}
