using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using KurrentDB.Client;
using MicroPlumberd.Migration;
using MicroPlumberd.Rewrite;
using Xunit;
using Xunit.Abstractions;

namespace MicroPlumberd.Rewrite.Tests;

/// <summary>
/// E2E-01..12 — the tool against real KurrentDB containers, per `iteration-1/test-scenarios.md`.
/// </summary>
/// <remarks>
/// <para>Every assertion reads the store through the ORIGINAL container after its restart. Reading the scratch
/// store would prove only that the copy engine can write — not that the operator ends up with the rewritten
/// history under the container they started with.</para>
/// <para>Each scenario disposes its seeding client before invoking the tool (E2E-09 excepted, where the open
/// subscription IS the scenario) — otherwise the connected-client guard would be catching the test itself.</para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection("rewrite-e2e")]
public class RewriteE2ETests(ITestOutputHelper output)
{
    private static RewriteOptions Options(RewriteFixture f) => new()
    {
        Container = f.ContainerName,
        Yes = true,
        Output = new StringWriter()
    };

    private async Task<RewriteReport> RunAsync(RewriteFixture f, RewriteOptions options)
    {
        var report = await RewriteCommand.RunAsync(options, f.Docker, f.Loggers);
        output.WriteLine(report.Format());
        return report;
    }

    /// <summary>Runs a rewrite after making sure no client of the TEST's own is still holding the store.</summary>
    private async Task<RewriteReport> RewriteAsync(RewriteFixture f, RewriteOptions options)
    {
        await f.WaitUntilNoClientsAsync(TimeSpan.FromSeconds(30));
        return await RunAsync(f, options);
    }

    // ================================================================= E2E-01

    [Fact]
    public async Task E2E01_A_pure_copy_repairs_a_projection_faulted_by_a_dangling_link()
    {
        await using var f = await RewriteFixture.StartAsync(output);
        await f.AssertDanglingLinkExistsAsync();

        // The precondition the scenario is named for, established rather than assumed: an unguarded linkTo
        // projection really is Faulted on this store, and the guarded one really is not.
        var before = await f.WaitForFaultedAsync(RewriteFixture.FaultyProjection, TimeSpan.FromSeconds(60));
        before.Should().Contain("Faulted",
            "the scenario is about repairing a FAULTED projection — if the fixture never faulted one, the "
            + "'Running afterwards' assertion below would pass against a store that was never broken");
        (await f.ProjectionStatusAsync(RewriteFixture.MergeStream)).Should().Contain("Running");

        var order1Before = await f.ReadAsync("Order-1");

        var report = await RewriteAsync(f, Options(f));
        report.Code.Should().Be(ExitCode.Ok);

        // The container is back on P/data and the pre-rewrite store is kept beside it.
        f.RootEntries().Should().Contain("data")
            .And.ContainSingle(e => e.StartsWith("data.bak."), "the old store is never deleted");
        report.BackupDir.Should().NotBeNull();

        // History survived byte-for-byte, read through the original container.
        var order1After = await f.ReadAsync("Order-1");
        order1After.Select(e => e.EventType).Should().Equal(["OrderCreated", "LineAdded", "LineAdded"]);
        order1After.Select(e => e.EventId).Should().Equal(order1Before.Select(e => e.EventId),
            "a pure copy preserves every event id");
        order1After.Select(e => e.Data.ToArray()).Should()
            .BeEquivalentTo(order1Before.Select(e => e.Data.ToArray()), o => o.WithStrictOrdering());

        // The repair itself.
        await using (var client = f.NewClient())
        {
            var (total, dangling) = await RewriteFixture.CountLinksAsync(client, "$et-LineAdded");
            output.WriteLine($"after rewrite: $et-LineAdded has {total} link(s), {dangling} dangling");
            total.Should().BeGreaterThan(0, "zero links would satisfy 'none dangling' vacuously");
            dangling.Should().Be(0, "every link in the rewritten store must resolve");
        }

        var faultyAfter = await f.WaitForRunningAsync(RewriteFixture.FaultyProjection, TimeSpan.FromSeconds(60));
        faultyAfter.Should().Contain("Running").And.NotContain("Faulted",
            "the projection that was Faulted on the old store runs on the rewritten one — that is the repair");
        (await f.ProjectionStatusAsync(RewriteFixture.MergeStream)).Should().Contain("Running");

        // uid 1001 in the container vs the operator's uid on the host: the swapped-in directory must stay
        // writable by the container's user, and the only proof of that is a write.
        await using (var client = f.NewClient())
        {
            var write = async () => await client.AppendToStreamAsync("PostSwap-1", StreamState.NoStream,
                [new EventData(Uuid.NewUuid(), "AfterSwap", Encoding.UTF8.GetBytes("""{"ok":true}"""))]);
            await write.Should().NotThrowAsync(
                "the container must still be able to write to the store it was handed, whatever uid it runs as");
        }
    }

    // ================================================================= E2E-02

    [Fact]
    public async Task E2E02_dropStream_removes_the_named_streams_and_leaves_every_other_one_intact()
    {
        await using var f = await RewriteFixture.StartAsync(output);
        var order1Before = await f.ReadAsync("Order-1");
        var order2Before = await f.ReadAsync("Order-2");
        (await f.ReadAsync("Junk-1")).Should().HaveCount(2, "the stream this scenario drops must be there first");

        var report = await RewriteAsync(f, Options(f) with { Eval = "dropStream(/^Junk-/)" });

        report.Code.Should().Be(ExitCode.Ok);
        (await f.StreamExistsAsync("Junk-1")).Should().BeFalse("the dropped stream must not exist in the new store");
        report.DroppedStreams.Select(d => d.Stream).Should().Contain("Junk-1");

        (await f.ReadAsync("Order-1")).Select(e => e.EventId).Should().Equal(order1Before.Select(e => e.EventId));
        (await f.ReadAsync("Order-2")).Select(e => e.EventId).Should().Equal(order2Before.Select(e => e.EventId));
        (await f.ReadAsync("Cmd-1")).Should().ContainSingle();
    }

    // ================================================================= E2E-03

    [Fact]
    public async Task E2E03_dropEvent_removes_one_event_and_the_survivors_are_renumbered_gaplessly()
    {
        await using var f = await RewriteFixture.StartAsync(output);
        var before = await f.ReadAsync("Order-1");

        // The stream is named in the rule so this variant and the transform variant below express exactly the
        // same intent — `EventNumber === 1` alone would also hit Order-2's second event.
        var report = await RewriteAsync(f, Options(f) with
        {
            Eval = """dropEvent(function(e){ return e.Stream === "Order-1" && e.EventType === "LineAdded" && e.EventNumber === 1; });"""
        });

        report.Code.Should().Be(ExitCode.Ok);
        await AssertOrder1LostItsFirstLineAsync(f, before);
        (await f.ReadAsync("Order-2")).Should().HaveCount(2, "a rule naming Order-1 must not touch Order-2");
    }

    [Fact]
    public async Task E2E03_a_transform_returning_undefined_drops_the_same_event_as_dropEvent()
    {
        await using var f = await RewriteFixture.StartAsync(output);
        var before = await f.ReadAsync("Order-1");

        var report = await RewriteAsync(f, Options(f) with
        {
            Eval = """function transform(o){ return (o.Stream === "Order-1" && o.EventNumber === 1) ? undefined : o; }"""
        });

        report.Code.Should().Be(ExitCode.Ok);
        await AssertOrder1LostItsFirstLineAsync(f, before);
    }

    private static async Task AssertOrder1LostItsFirstLineAsync(RewriteFixture f, List<EventRecord> before)
    {
        var after = await f.ReadAsync("Order-1");
        after.Select(e => e.EventNumber.ToUInt64()).Should().Equal([0ul, 1ul],
            "the survivors are renumbered gaplessly — a gap would break every reader that expects a version");
        after.Select(e => e.EventType).Should().Equal(["OrderCreated", "LineAdded"]);
        after.Select(e => e.EventId).Should().Equal([before[0].EventId, before[2].EventId],
            "the surviving events keep their own ids; only the dropped one is gone");
    }

    // ================================================================= E2E-04

    [Fact]
    public async Task E2E04_update_by_type_and_by_id_change_only_what_the_script_named()
    {
        await using var f = await RewriteFixture.StartAsync(output);

        var report = await RewriteAsync(f, Options(f) with
        {
            Eval = $$"""
                update("OrderCreated", function(e){ e.Data.Customer = "X"; return e; });
                updateById("{{RewriteFixture.CommandEventId.ToGuid()}}", function(e){ e.Metadata.Note = "n"; return e; });
                """
        });

        report.Code.Should().Be(ExitCode.Ok);

        var order = (await f.ReadAsync("Order-1"))[0];
        var data = JsonNode.Parse(order.Data.Span)!.AsObject();
        data["Customer"]!.GetValue<string>().Should().Be("X");
        data["Total"]!.GetValue<int>().Should().Be(10, "a field the rule did not name must survive untouched");

        var cmd = (await f.ReadAsync("Cmd-1")).Single();
        cmd.EventId.Should().Be(RewriteFixture.CommandEventId, "the event id is preserved across a rewrite");
        var meta = JsonNode.Parse(cmd.Metadata.Span)!.AsObject();
        meta["Note"]!.GetValue<string>().Should().Be("n");
        meta["$correlationId"]!.GetValue<string>().Should().Be("11111111-1111-1111-1111-111111111111");
        meta["$causationId"]!.GetValue<string>().Should().Be("22222222-2222-2222-2222-222222222222");

        // The by-type rule must not have leaked onto the command's payload.
        JsonNode.Parse(cmd.Data.Span)!["Reason"]!.GetValue<string>().Should().Be("manual");
    }

    // ================================================================= E2E-05

    [Fact]
    public async Task E2E05_a_script_file_containing_only_the_Replicator_transform_runs_unchanged()
    {
        await using var f = await RewriteFixture.StartAsync(output);
        var scriptPath = Path.Combine(Path.GetTempPath(), $"mp-rewrite-{Guid.NewGuid():N}.js");
        // Nothing but `function transform(original)` — the contract Kurrent Replicator documents, with none
        // of this library's helpers, so a script written for either tool runs on both.
        await File.WriteAllTextAsync(scriptPath, """
            function transform(original) {
                if (original.Stream === "Order-2") {
                    original.EventType = "V2." + original.EventType;
                }
                return original;
            }
            """);
        try
        {
            var report = await RewriteAsync(f, Options(f) with { ScriptPath = scriptPath });

            report.Code.Should().Be(ExitCode.Ok);
            (await f.ReadAsync("Order-2")).Select(e => e.EventType)
                .Should().Equal(["V2.OrderCreated", "V2.LineAdded"]);
            (await f.ReadAsync("Order-1")).Select(e => e.EventType)
                .Should().Equal(["OrderCreated", "LineAdded", "LineAdded"], "other streams are untouched");
        }
        finally
        {
            File.Delete(scriptPath);
        }
    }

    // ================================================================= E2E-06

    [Fact]
    public async Task E2E06_a_dry_run_reports_what_it_would_do_and_leaves_everything_exactly_as_it_was()
    {
        await using var f = await RewriteFixture.StartAsync(output);
        var startedAt = await f.StartedAtAsync();
        var junkBefore = await f.ReadAsync("Junk-1");

        var report = await RewriteAsync(f, Options(f) with { Eval = "dropStream(\"Junk-1\")", DryRun = true });

        report.Code.Should().Be(ExitCode.Ok);
        report.DryRun.Should().BeTrue();
        report.DroppedStreams.Should().Contain(d => d.Stream == "Junk-1" && d.Events == 2,
            "a dry run's whole value is naming the affected streams and their counts");

        f.RootEntries().Should().Equal(["data"],
            "a dry run leaves no new-store directory behind — not even the one it had to create to run");
        (await f.ScratchContainersAsync()).Should().BeEmpty("the scratch container is removed on a dry run too");
        (await f.StartedAtAsync()).Should().Be(startedAt, "a dry run never stops the container");

        (await f.ReadAsync("Junk-1")).Select(e => e.EventId).Should().Equal(junkBefore.Select(e => e.EventId),
            "nothing was written, so the store still holds the stream the rule would have dropped");
    }

    // ================================================================= E2E-07

    [Fact]
    public async Task E2E07_rollback_puts_the_pre_rewrite_store_back()
    {
        await using var f = await RewriteFixture.StartAsync(output);
        var junkBefore = await f.ReadAsync("Junk-1");

        (await RewriteAsync(f, Options(f) with { Eval = "dropStream(\"Junk-1\")" })).Code.Should().Be(ExitCode.Ok);
        (await f.StreamExistsAsync("Junk-1")).Should().BeFalse("the rewrite must have taken effect first — "
            + "rolling back a store that was never changed would prove nothing");

        var report = await RewriteAsync(f, Options(f) with { Mode = RewriteMode.Rollback });

        report.Code.Should().Be(ExitCode.Ok);
        (await f.ReadAsync("Junk-1")).Select(e => e.EventId).Should().Equal(junkBefore.Select(e => e.EventId),
            "the old history is back, event ids and all");
        f.RootEntries().Should().Contain(e => e.StartsWith("data.rolledback."),
            "the rewritten store is kept, not deleted — a rollback must itself be reversible");
        (await f.ReadAsync("Order-1")).Should().HaveCount(3);
    }

    // ================================================================= E2E-08

    [Fact]
    public async Task E2E08_a_running_sibling_of_the_same_compose_project_is_refused()
    {
        await using var f = await RewriteFixture.StartAsync(output);
        var sibling = await f.StartSiblingAsync();
        var before = f.RootEntries();

        var report = await RewriteAsync(f, Options(f));

        report.Code.Should().Be(ExitCode.GuardRefusal);
        report.Headline.Should().Contain(sibling, "the operator has to be told WHICH container to stop");
        f.RootEntries().Should().Equal(before, "a refusal creates nothing — not even the new-store directory");
        (await f.ScratchContainersAsync()).Should().BeEmpty("the guard runs before any container is started");
        (await f.ReadAsync("Order-1")).Should().HaveCount(3, "the store is untouched");
    }

    // ================================================================= E2E-09

    [Fact]
    public async Task E2E09_a_connected_client_is_refused_and_the_count_is_reported()
    {
        await using var f = await RewriteFixture.StartAsync(output);
        var before = f.RootEntries();

        // The one scenario that deliberately keeps a client open while the tool runs.
        await using var client = f.NewClient();
        using var cts = new CancellationTokenSource();
        var subscription = Task.Run(async () =>
        {
            try
            {
                await foreach (var _ in client.SubscribeToAll(FromAll.Start, cancellationToken: cts.Token)) { }
            }
            // The client surfaces its own cancellation as an RpcException(Cancelled), not as
            // OperationCanceledException — catching only the latter turns tearing the subscription down into
            // a test failure that says nothing about the tool.
            catch (OperationCanceledException) { }
            catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.Cancelled) { }
        });
        await WaitUntilOpenCallsAsync(f, atLeast: 1, TimeSpan.FromSeconds(30));

        var report = await RunAsync(f, Options(f));

        report.Code.Should().Be(ExitCode.GuardRefusal);
        report.Headline.Should().Contain(DockerStore.OpenGrpcCallsMetric)
            .And.Contain("gRPC call", "the refusal must say what it counted, not merely that it refused");
        f.RootEntries().Should().Equal(before);
        (await f.ScratchContainersAsync()).Should().BeEmpty();

        await cts.CancelAsync();
        await subscription;
    }

    private static async Task WaitUntilOpenCallsAsync(RewriteFixture f, int atLeast, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var last = 0;
        while (DateTime.UtcNow < deadline)
        {
            (last, _) = await DockerStore.ReadConnectionMetricsAsync(f.ConnectionString);
            if (last >= atLeast) return;
            await Task.Delay(200);
        }
        throw new InvalidOperationException(
            $"The subscription never registered as an open gRPC call ({last} < {atLeast}); the scenario would "
            + "otherwise 'pass' against a store with no client connected at all.");
    }

    // ================================================================= E2E-10

    [Fact]
    public async Task E2E10_a_failure_after_the_swap_restores_the_original_store_and_restarts_the_container()
    {
        await using var f = await RewriteFixture.StartAsync(output);
        var junkBefore = await f.ReadAsync("Junk-1");

        var report = await RewriteAsync(f, Options(f) with
        {
            Eval = "dropStream(\"Junk-1\")",
            FailAfterSwapForTest = true
        });

        report.Code.Should().Be(ExitCode.FilesystemFailure);
        report.Headline.Should().Contain("restored");

        // The whole point: the operator's data is what is live, not the half-finished rewrite.
        (await f.ReadAsync("Junk-1")).Select(e => e.EventId).Should().Equal(junkBefore.Select(e => e.EventId),
            "the ORIGINAL store is back in place, including the stream the rewrite would have dropped");
        (await f.ReadAsync("Order-1")).Should().HaveCount(3);
        f.RootEntries().Should().Equal(["data"],
            "the restore leaves no data.new.* and no data.bak.* — the rewrite is as if it had never run");

        await using var client = f.NewClient();
        var write = async () => await client.AppendToStreamAsync("PostRestore-1", StreamState.NoStream,
            [new EventData(Uuid.NewUuid(), "AfterRestore", Encoding.UTF8.GetBytes("""{"ok":true}"""))]);
        await write.Should().NotThrowAsync("the container is running on the restored store and can write to it");
    }

    // ================================================================= E2E-11

    [Fact]
    public async Task E2E11_status_reports_the_store_its_backups_and_whether_a_rewrite_is_safe_now()
    {
        await using var f = await RewriteFixture.StartAsync(output);
        await f.WaitUntilNoClientsAsync(TimeSpan.FromSeconds(30));
        var before = f.RootEntries();

        var report = await RunAsync(f, Options(f) with { Mode = RewriteMode.Status });
        var text = report.Format();

        report.Code.Should().Be(ExitCode.Ok);
        report.Container!.Image.Should().Be(RewriteFixture.Image);
        report.Container.Data.StoreDir.Should().Be(f.StoreDir);
        text.Should().Contain("backups").And.Contain("(none)", "this store has never been rewritten");
        text.Should().Contain("safe         : yes");

        f.RootEntries().Should().Equal(before, "status changes nothing");
        (await f.ScratchContainersAsync()).Should().BeEmpty();

        // And it says NO when a guard would refuse — the answer that actually matters to an operator.
        var sibling = await f.StartSiblingAsync();
        var blocked = await RunAsync(f, Options(f) with { Mode = RewriteMode.Status });
        blocked.Format().Should().Contain("safe         : no").And.Contain(sibling);
    }

    // ================================================================= E2E-12

    [Fact]
    public async Task E2E12_the_new_store_carries_a_history_record_of_the_rewrite_that_produced_it()
    {
        await using var f = await RewriteFixture.StartAsync(output);
        const string script = "dropStream(/^Junk-/)";

        var report = await RewriteAsync(f, Options(f) with { Eval = script });
        report.Code.Should().Be(ExitCode.Ok);

        var history = await f.ReadAsync("mp-migrations");
        history.Should().ContainSingle("one run, one record");
        history[0].EventType.Should().Be(nameof(MigrationApplied));

        var record = JsonSerializer.Deserialize<MigrationApplied>(history[0].Data.Span)!;
        record.Id.Should().Be(report.MigrationId);
        record.Id.Should().StartWith("rewrite_", "each run is its own migration, stamped with when it ran");
        record.Checksum.Should().Be(Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(script))),
            "the history carries the hash of the SCRIPT — the only thing that identifies these rules");
        record.Descriptors.Should().NotBeNull()
            .And.Contain(d => d.StartsWith("DropStreamPredicate"),
                "an operator reading this store months later must see WHAT the rewrite did");
        record.Dropped.Should().Be(2, "Junk-1 held two events");
    }

    // ================================================================= exit codes

    [Fact]
    public async Task A_script_that_does_not_parse_exits_2_before_any_container_is_touched()
    {
        await using var f = await RewriteFixture.StartAsync(output);
        var before = f.RootEntries();
        var startedAt = await f.StartedAtAsync();

        var report = await RunAsync(f, Options(f) with { Eval = "function transform(o) { return o;" });

        report.Code.Should().Be(ExitCode.ScriptError);
        report.Headline.Should().Contain("line").And.Contain("column");
        f.RootEntries().Should().Equal(before);
        (await f.ScratchContainersAsync()).Should().BeEmpty("the script is parsed before docker is called at all");
        (await f.StartedAtAsync()).Should().Be(startedAt, "the container was never even stopped");
    }

    [Fact]
    public async Task An_unknown_container_exits_3()
    {
        await using var f = await RewriteFixture.StartAsync(output);

        var report = await RunAsync(f, Options(f) with { Container = $"mp-rewrite-test-absent-{Guid.NewGuid():N}" });

        report.Code.Should().Be(ExitCode.DockerUnavailable);
        report.Headline.Should().Contain("No such container");
    }
}

/// <summary>
/// Every end-to-end scenario runs alone. They start real containers on a shared docker host and the sibling
/// guard is about containers being up — two of them at once would refuse each other.
/// </summary>
[CollectionDefinition("rewrite-e2e", DisableParallelization = true)]
public sealed class RewriteE2ECollection;
