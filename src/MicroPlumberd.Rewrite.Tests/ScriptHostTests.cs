using System.Diagnostics;
using System.Text.Json.Nodes;
using FluentAssertions;
using MicroPlumberd.Migration;
using MicroPlumberd.Migration.Scripting;
using Xunit;

namespace MicroPlumberd.Rewrite.Tests;

/// <summary>
/// UT-01..05 — the JavaScript rule host, in-process, no store and no container.
/// </summary>
public class ScriptHostTests
{
    // ------------------------------------------------------------------ UT-01

    /// <summary>
    /// Every JSON shape a payload can hold has to survive the trip into JavaScript and back. The script here
    /// ADDS one field, which forces the real round-trip: the reference-equality fast path (asserted in the
    /// test below) would otherwise return the original node and prove nothing about marshalling.
    /// </summary>
    [Fact]
    public void UT01_Data_of_every_JSON_kind_survives_the_round_trip_into_the_script()
    {
        const string payload = """
            {"int":42,"negative":-7,"zero":0,"fraction":1.5,"bool":true,"falsy":false,
             "text":"a \"quoted\" \\ back\\slash é ł",
             "nothing":null,"empty":{},"emptyArray":[],
             "array":[1,"two",null,{"deep":[true,2.25]}],
             "nested":{"a":{"b":{"c":"d"}}}}
            """;
        var input = Ev.Make(data: payload);
        var runner = new ScriptRunner("function transform(o){ o.Data.marker = 1; return o; }");

        var result = runner.Run(input);

        result.Should().NotBeNull();
        var data = result!.Data!.AsObject();
        data["marker"]!.GetValue<int>().Should().Be(1, "the script's own write must be there — otherwise "
            + "this test would pass on a host that never ran the script at all");

        data.Remove("marker");
        // Compare against the source payload re-parsed and re-rendered the same way, so the comparison is
        // about VALUES and key order, not about the whitespace the literal above is formatted with.
        data.ToJsonString().Should().Be(JsonNode.Parse(payload)!.ToJsonString());
    }

    /// <summary>
    /// The fidelity guarantee behind a pure copy: a script that does not touch the payload must hand back the
    /// SAME node instance, which is how the copy engine knows to write the original bytes byte-for-byte
    /// instead of re-rendering the JSON. Break the reference check and this goes red.
    /// </summary>
    [Fact]
    public void UT01_An_untouched_payload_comes_back_as_the_very_same_node_so_the_copy_stays_byte_identical()
    {
        var input = Ev.Make(data: """{"n":1.0,"s":"x"}""");
        var runner = new ScriptRunner("function transform(o){ return o; }");

        var result = runner.Run(input);

        result.Should().BeSameAs(input, "an identity transform changed nothing, so nothing may be re-serialised");
    }

    /// <summary>
    /// The known limit of scripting a JSON payload: JavaScript has one number type, so an integer beyond
    /// 2^53 cannot survive a script that touches the payload. This test PINS that behaviour rather than
    /// claiming a fidelity we do not have — it is the warning in the README, made executable.
    /// </summary>
    [Fact]
    public void UT01_An_integer_beyond_2_pow_53_is_rounded_when_the_script_touches_the_payload()
    {
        var input = Ev.Make(data: """{"id":9007199254740993}""");
        var untouched = new ScriptRunner("function transform(o){ return o; }").Run(input);
        var touched = new ScriptRunner("function transform(o){ o.Data.marker = 1; return o; }").Run(input);

        untouched!.Data!.ToJsonString().Should().Contain("9007199254740993",
            "an untouched payload never goes through JavaScript's number type");
        touched!.Data!["id"]!.GetValue<long>().Should().Be(9007199254740992L,
            "once the script touches the payload the whole object is re-rendered from JS doubles");
    }

    // ------------------------------------------------------------------ UT-02

    [Theory]
    [InlineData("function transform(o){ o.Stream = ''; return o; }")]
    [InlineData("function transform(o){ o.EventType = ''; return o; }")]
    [InlineData("function transform(o){ return undefined; }")]
    public void UT02_An_empty_Stream_or_EventType_drops_the_event_just_like_returning_undefined(string script)
    {
        new ScriptRunner(script).Run(Ev.Make()).Should().BeNull();
    }

    [Fact]
    public void UT02_A_transform_returning_a_non_object_fails_naming_the_event_it_was_processing()
    {
        var runner = new ScriptRunner("function transform(o){ return 'oops'; }");

        var act = () => runner.Run(Ev.Make(stream: "Order-7", number: 3));

        act.Should().Throw<ScriptExecutionException>()
            .Where(e => e.Stream == "Order-7" && e.EventNumber == 3)
            .WithMessage("*Order-7#3*")
            .WithMessage("*oops*", "an operator must see WHAT the script returned, not just that it was wrong");
    }

    // ------------------------------------------------------------------ UT-03

    [Fact]
    public void UT03_dropStream_is_declared_before_the_per_event_rule_so_a_dropped_stream_never_reaches_update()
    {
        var runner = new ScriptRunner("""
            dropStream(/^Junk-/);
            update("OrderCreated", function(e){ e.Data.seen = true; return e; });
            """);

        runner.Calls.Should().Equal(["DropStream(predicate)", "Transform"],
            "the copy engine applies operations in declaration order and short-circuits on a drop, so the "
            + "stream drop MUST be registered first for update never to see those events");
        runner.StreamDrops.Should().ContainSingle();
        runner.StreamDrops[0]("Junk-1").Should().BeTrue();
        runner.StreamDrops[0]("Order-1").Should().BeFalse();
    }

    [Fact]
    public void UT03_transform_sees_the_event_as_update_left_it()
    {
        var runner = new ScriptRunner("""
            update("OrderCreated", function(e){ e.Data.stage = "updated"; return e; });
            function transform(o){ o.Data.sawStage = o.Data.stage; return o; }
            """);

        var result = runner.Run(Ev.Make(type: "OrderCreated", data: """{"n":1}"""));

        result!.Data!["stage"]!.GetValue<string>().Should().Be("updated");
        result.Data["sawStage"]!.GetValue<string>().Should().Be("updated",
            "transform runs last and must observe the update's result, not the original payload");
    }

    [Fact]
    public void UT03_update_only_fires_for_its_own_event_type()
    {
        var runner = new ScriptRunner("""update("OrderCreated", function(e){ e.Data.touched = true; return e; });""");

        var matched = runner.Run(Ev.Make(type: "OrderCreated"));
        var other = runner.Run(Ev.Make(type: "LineAdded"));

        matched!.Data!["touched"].Should().NotBeNull();
        other!.Data!["touched"].Should().BeNull();
    }

    [Fact]
    public void UT03_updateById_fires_only_for_the_matching_event_id()
    {
        var runner = new ScriptRunner(
            "updateById(\"" + Ev.KnownId + "\", function(e){ e.Metadata.Note = \"n\"; return e; });");

        var matched = runner.Run(Ev.Make(id: Ev.KnownId));
        var other = runner.Run(Ev.Make(id: Guid.NewGuid()));

        matched!.Metadata!["Note"]!.GetValue<string>().Should().Be("n");
        matched.Metadata["$correlationId"]!.GetValue<string>().Should().Be("c",
            "an update must not disturb the fields it did not name");
        other!.Metadata!["Note"].Should().BeNull();
    }

    [Fact]
    public void UT03_dropEvent_can_match_on_the_source_write_date_as_an_ISO_string()
    {
        var runner = new ScriptRunner("""dropEvent(function(e){ return e.Created < "2026-08-25"; });""");

        runner.Run(Ev.Make(created: new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc))).Should().BeNull();
        runner.Run(Ev.Make(created: new DateTime(2026, 8, 30, 0, 0, 0, DateTimeKind.Utc))).Should().NotBeNull();
    }

    [Fact]
    public void UT03_renameType_and_renameStream_are_declared_as_native_operations_not_folded_into_the_transform()
    {
        var runner = new ScriptRunner("""
            renameStream("Pipeline-old", "Pipeline-new");
            renameType("PipelineStarted", "PipelineStartedV2");
            """);

        runner.Calls.Should().Equal([
            "RenameStream(\"Pipeline-old\"->\"Pipeline-new\")",
            "RenameType(\"PipelineStarted\"->\"PipelineStartedV2\")"
        ], "a rename needs no per-event script call, so a script with only renames declares no Transform at all");
    }

    // ------------------------------------------------------------------ UT-04

    [Fact]
    public void UT04_A_syntax_error_fails_construction_with_the_line_and_column()
    {
        var script = "dropStream(\"A\");\nfunction transform(o) { return o; \n";

        var act = () => new ScriptMigration(script, "rewrite_20260907T101500");

        var ex = act.Should().Throw<ScriptSyntaxException>().Which;
        ex.Line.Should().BeGreaterThan(0);
        ex.Column.Should().BeGreaterThan(0);
        ex.Message.Should().Contain($"line {ex.Line}").And.Contain($"column {ex.Column}",
            "the operator fixes the file by coordinate — 'the script is invalid' is not actionable");
    }

    [Fact]
    public void UT04_A_valid_script_yields_an_id_carrying_the_prefix_and_the_script_hash()
    {
        const string script = "dropStream(\"Junk-1\");";
        var m = new ScriptMigration(script, "rewrite_20260907T101500");

        m.Id.Should().StartWith("rewrite_20260907T101500_");
        m.Id.Should().Be($"rewrite_20260907T101500_{m.ScriptChecksum[..12]}");
        m.ChecksumOverride.Should().Be(m.ScriptChecksum);

        // Two DIFFERENT scripts must never share a checksum — the history guard is the only thing standing
        // between "already applied" and a store rewritten by different rules.
        new ScriptMigration("dropStream(\"Junk-2\");", "rewrite_20260907T101500").ScriptChecksum
            .Should().NotBe(m.ScriptChecksum);
    }

    // ------------------------------------------------------------------ UT-05

    [Fact]
    public void UT05_A_script_that_never_returns_is_aborted_and_the_failure_names_the_event()
    {
        var runner = new ScriptRunner("function transform(o){ while(true){} }");
        var sw = Stopwatch.StartNew();

        var act = () => runner.Run(Ev.Make(stream: "Order-9", number: 5));

        act.Should().Throw<ScriptExecutionException>()
            .Where(e => e.Stream == "Order-9" && e.EventNumber == 5)
            .WithMessage("*Order-9#5*")
            .WithInnerException<TimeoutException>();
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30),
            "the budget is per event — a runaway script must not hang the whole rewrite");
    }

    /// <summary>
    /// The budget belongs to the EVENT, not to each entry into JavaScript. Jint resets its own timeout at
    /// every call, so three half-budget helpers would sail through; this pins that they do not.
    /// </summary>
    [Fact]
    public void UT05_The_time_budget_is_shared_by_every_script_call_made_for_one_event()
    {
        const string spin = "function(e){ var t = Date.now(); while (Date.now() - t < 900) {} return e; }";
        var script = string.Concat(Enumerable.Repeat("update(\"Slow\", " + spin + ");\n", 3));
        var runner = new ScriptRunner(script);

        var act = () => runner.Run(Ev.Make(type: "Slow", stream: "Slow-1", number: 0));

        act.Should().Throw<ScriptExecutionException>()
            .WithInnerException<TimeoutException>("three 0.9 s handlers exceed one 2 s per-event budget, "
                + "even though no single call does");
    }

    /// <summary>
    /// The sandbox: a script cannot reach .NET. If CLR interop were ever switched on, this stops being an
    /// error and the tool would hand every operator's script the filesystem.
    /// </summary>
    [Fact]
    public void The_script_cannot_reach_the_CLR()
    {
        var runner = new ScriptRunner("function transform(o){ o.Data.host = System.IO.File.name; return o; }");

        var act = () => runner.Run(Ev.Make());

        act.Should().Throw<ScriptExecutionException>()
            .WithMessage("*System*", "there must be no 'System' in the script's world at all");
    }
}
