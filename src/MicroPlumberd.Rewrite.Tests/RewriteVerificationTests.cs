using FluentAssertions;
using MicroPlumberd.Migration;
using MicroPlumberd.Rewrite;
using Xunit;

namespace MicroPlumberd.Rewrite.Tests;

/// <summary>
/// The verification gate's logic, over real <see cref="CopyResult"/> shapes. These are what prove the gate
/// DISCRIMINATES; the end-to-end test proves the abort path around it.
/// </summary>
public class RewriteVerificationTests
{
    private static CopyResult Copy(params (string Stream, long Source, long Kept, long Dropped, string Target)[] rows)
    {
        var r = new CopyResult
        {
            DryRun = false,
            RunTimeUtc = DateTime.UtcNow,
            MigrationStats = new Dictionary<string, MigrationStats>()
        };
        foreach (var (stream, src, kept, dropped, target) in rows)
            r.SourceStreams[stream] = new StreamCopyInfo
            {
                TargetStream = target, SourceCount = src, Kept = kept, Dropped = dropped
            };
        return r;
    }

    private static RuleReach DropsStream(string name) =>
        new([s => s == name], [], HasEventLevelDrop: false);

    [Fact]
    public void A_faithful_copy_raises_nothing()
    {
        var copy = Copy(("Order-1", 3, 3, 0, "Order-1"), ("Junk-1", 2, 0, 2, "Junk-1"));
        var truth = new Dictionary<string, long> { ["Order-1"] = 3, ["Junk-1"] = 2 };

        RewriteVerification.Check(copy, truth, DropsStream("Junk-1")).Should().BeEmpty();
    }

    [Fact]
    public void An_engine_that_read_fewer_events_than_the_source_holds_is_caught()
    {
        // The defect the library's verifier is structurally blind to: the engine's expectation and its result
        // fall together, so the destination matches it exactly and RESULT: OK is printed over a partial copy.
        var copy = Copy(("Order-1", 2, 2, 0, "Order-1"));
        var truth = new Dictionary<string, long> { ["Order-1"] = 3 };

        var issues = RewriteVerification.Check(copy, truth, RuleReach.None);

        issues.Should().ContainSingle().Which.Reason.Should().Contain("3").And.Contain("2");
    }

    [Fact]
    public void A_stream_the_engine_never_enumerated_at_all_is_caught()
    {
        var copy = Copy(("Order-1", 3, 3, 0, "Order-1"));
        var truth = new Dictionary<string, long> { ["Order-1"] = 3, ["Forgotten-1"] = 4 };

        RewriteVerification.Check(copy, truth, RuleReach.None)
            .Should().ContainSingle().Which.Subject.Should().Be("Forgotten-1");
    }

    [Fact]
    public void A_stream_that_vanished_with_no_rule_to_explain_it_is_caught()
    {
        // On a pure copy nothing may vanish at all — and this is precisely the case the library's verifier
        // reports to the operator as a "fully-dropped source stream (intended)".
        var copy = Copy(("Junk-1", 2, 0, 2, "Junk-1"));
        var truth = new Dictionary<string, long> { ["Junk-1"] = 2 };

        RewriteVerification.Check(copy, truth, RuleReach.None)
            .Should().ContainSingle().Which.Reason.Should().Contain("no rule accounts for it");
    }

    [Fact]
    public void The_same_vanished_stream_is_accepted_when_a_dropStream_rule_names_it()
    {
        // The control for the test above: the gate must not simply refuse everything that vanished, or it
        // would block every legitimate dropStream run and tell us nothing.
        var copy = Copy(("Junk-1", 2, 0, 2, "Junk-1"));
        var truth = new Dictionary<string, long> { ["Junk-1"] = 2 };

        RewriteVerification.Check(copy, truth, DropsStream("Junk-1")).Should().BeEmpty();
    }

    [Fact]
    public void An_event_level_rule_makes_any_disappearance_attributable()
    {
        // A dropEvent or a transform can empty any stream one event at a time and cannot be asked about a
        // specific stream without replaying the copy. Claiming otherwise would produce false refusals, which
        // for a tool with no override means blocking a repair.
        var copy = Copy(("Order-1", 3, 0, 3, "Order-1"));
        var truth = new Dictionary<string, long> { ["Order-1"] = 3 };

        RewriteVerification.Check(copy, truth, new RuleReach([], [], HasEventLevelDrop: true))
            .Should().BeEmpty();
    }

    [Fact]
    public void A_stream_dropped_under_the_name_a_rename_gave_it_is_still_explained()
    {
        var copy = Copy(("Old-1", 2, 0, 2, "Old-1"));
        var truth = new Dictionary<string, long> { ["Old-1"] = 2 };
        var rules = new RuleReach([s => s == "New-1"], [("Old-1", "New-1")], HasEventLevelDrop: false);

        RewriteVerification.Check(copy, truth, rules).Should().BeEmpty();
    }
}
