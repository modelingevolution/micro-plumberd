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
    /// <summary>Uppercase hex SHA-256 of the empty string — the checksum a script-less run records.</summary>
    private static readonly string EmptyScriptSha256 =
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData([]));

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

        // requirements.md § Safety and lead decision 4: EVERY run records history. A pure copy is the tool's
        // headline invocation — the one an operator reaches for at 3 a.m. — and it must not be the one that
        // leaves no trace that the store they are looking at is not the original.
        var history = await f.ReadAsync("mp-migrations");
        history.Should().ContainSingle("a pure copy is still a run, and its history travels with the data");
        var applied = JsonSerializer.Deserialize<MigrationApplied>(history[0].Data.Span)!;
        applied.Id.Should().StartWith("rewrite_");
        applied.Checksum.Should().Be(EmptyScriptSha256,
            "a pure copy applied no script, so the checksum is the hash of an empty one — which is honest, "
            + "stays comparable with a scripted run, and is identical for every pure copy");
        applied.Descriptors.Should().NotBeNull().And.BeEmpty(
            "'no rules were recorded' and 'no rules ran' must not look the same to whoever reads this later");
        applied.SourceEvents.Should().Be(report.SourceEvents).And.BeGreaterThan(0);
        applied.Dropped.Should().Be(0);

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
        // Signalled once the subscription has actually been ESTABLISHED — the client connects lazily, so
        // "the task was started" is not the same thing, and waiting on the wrong one is a race.
        var established = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var subscription = Task.Run(async () =>
        {
            try
            {
                // From the START, so the fixture's existing events arrive at once: receiving one is direct
                // evidence the subscription is live, and needs no assumption about which control messages this
                // client surfaces through its default enumerator.
                await foreach (var _ in client.SubscribeToAll(FromAll.Start, cancellationToken: cts.Token))
                    established.TrySetResult();
            }
            // The client surfaces its own cancellation as an RpcException(Cancelled), not as
            // OperationCanceledException — catching only the latter turns tearing the subscription down into
            // a test failure that says nothing about the tool.
            catch (OperationCanceledException) { }
            catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.Cancelled) { }
            finally
            {
                // If it ended without ever being confirmed, unblock the wait below with the real reason
                // instead of letting it time out saying something that is true but not the cause.
                established.TrySetException(new InvalidOperationException(
                    "the $all subscription ended before it ever received an event"));
            }
        });

        // 60s, not 30: establishing a subscription is slower when this shared host has just run sixty
        // container-heavy scenarios, and the point of the wait is to reach the state the assertion needs.
        await WaitUntilOpenCallsAsync(f, subscription, established.Task, atLeast: 1, TimeSpan.FromSeconds(60));

        var report = await RunAsync(f, Options(f));

        report.Code.Should().Be(ExitCode.GuardRefusal);
        report.Headline.Should().Contain(DockerStore.OpenGrpcCallsMetric)
            .And.Contain("gRPC call", "the refusal must say what it counted, not merely that it refused");
        f.RootEntries().Should().Equal(before);
        (await f.ScratchContainersAsync()).Should().BeEmpty();

        await cts.CancelAsync();
        await subscription;
    }

    private async Task WaitUntilOpenCallsAsync(RewriteFixture f, Task subscription, Task established,
        int atLeast, TimeSpan timeout)
    {
        // Wait for the subscription to be CONFIRMED by the server first. Polling the metric without this is a
        // race — the subscribe call is only in flight once the client has actually connected, and under load
        // that takes long enough for a naive poll to give up and blame the store.
        var confirmed = await Task.WhenAny(established, subscription, Task.Delay(timeout));
        if (confirmed == established) await established;          // rethrows the real reason if it failed
        else if (confirmed == subscription) await subscription;   // ended early — surface ITS exception
        else throw new InvalidOperationException(
            $"The $all subscription received no event within {timeout.TotalSeconds:0}s, so it was never live.");

        var deadline = DateTime.UtcNow + timeout;
        var last = 0;
        while (DateTime.UtcNow < deadline)
        {
            (last, _) = await DockerStore.ReadConnectionMetricsAsync(f.ConnectionString);
            if (last >= atLeast) return;
            if (subscription.IsCompleted) await subscription;
            await Task.Delay(200);
        }
        throw new InvalidOperationException(
            $"The subscription is live but the store reports {last} open gRPC call(s) (< {atLeast}); the "
            + "scenario would otherwise 'pass' against a store with no client connected at all.");
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

    // ================================================================= exit 4

    /// <summary>
    /// M1 — the guards can only say "nobody is writing" at the instant they run, and between that instant and
    /// the swap sit an unbounded confirmation prompt and the whole copy, with the old store up and writable.
    /// An event appended in that window is not in the new store and the swap would destroy it, silently: the
    /// engine never saw it, so its own bookkeeping balances perfectly without it.
    /// </summary>
    /// <remarks>
    /// The write happens while the tool is blocked on the confirmation prompt, which makes the race the
    /// scenario is about deterministic — and needs no fault-injection hook, so what is exercised here is the
    /// real trigger rather than a simulation of one.
    /// </remarks>
    [Fact]
    public async Task A_write_to_the_source_during_the_run_aborts_before_the_swap_with_exit_4()
    {
        await using var f = await RewriteFixture.StartAsync(output);
        await f.WaitUntilNoClientsAsync(TimeSpan.FromSeconds(30));
        var before = f.RootEntries();
        var startedAt = await f.StartedAtAsync();

        using var writesThenConfirms = AppendsThenConfirms(f, "LateWriter-1");
        var report = await RunAsync(f, Options(f) with
        {
            Yes = false,
            ConfirmationInput = writesThenConfirms
        });

        writesThenConfirms.Acted.Should().BeTrue("the scenario is only meaningful if the write happened");
        report.Code.Should().Be(ExitCode.EngineFailure);
        report.Headline.Should().Contain("written to DURING the run")
            .And.Contain("old store is untouched");

        // "Untouched" has to mean it: no swap, no backup, no scratch directory, container never stopped, and
        // the late event still where the writer put it.
        f.RootEntries().Should().Equal(before);
        (await f.ScratchContainersAsync()).Should().BeEmpty();
        (await f.StartedAtAsync()).Should().Be(startedAt, "the container is never stopped before the gate passes");
        (await f.ReadAsync("LateWriter-1")).Should().ContainSingle("the write that caused the abort survived it");
        (await f.ReadAsync("Order-1")).Should().HaveCount(3);
    }

    /// <summary>
    /// Does something the moment the tool asks for confirmation, then answers it — which puts that something
    /// inside the window between the guards and the swap, deterministically, with no fault injection.
    /// </summary>
    private sealed class ActOnPromptThenAnswer(Action act, string answer) : TextReader
    {
        public bool Acted { get; private set; }

        public override string ReadLine()
        {
            if (!Acted) { act(); Acted = true; }
            return answer;
        }
    }

    private static ActOnPromptThenAnswer AppendsThenConfirms(RewriteFixture f, string stream) =>
        new(() =>
        {
            using var client = f.NewClient();
            client.AppendToStreamAsync(stream, StreamState.Any,
                [new EventData(Uuid.NewUuid(), "LateWrite", Encoding.UTF8.GetBytes("""{"late":true}"""))])
                .GetAwaiter().GetResult();
        }, "yes");

    /// <summary>
    /// The other half of M1: the guards are re-evaluated immediately before the swap. A sibling container
    /// brought up during the confirmation prompt passed the first check and must still be caught — nothing
    /// has been swapped yet, so refusing costs nothing.
    /// </summary>
    [Fact]
    public async Task A_sibling_started_during_the_run_is_caught_by_the_guard_re_check_before_the_swap()
    {
        await using var f = await RewriteFixture.StartAsync(output);
        await f.WaitUntilNoClientsAsync(TimeSpan.FromSeconds(30));
        var before = f.RootEntries();
        var startedAt = await f.StartedAtAsync();

        string sibling = "";
        using var startsSiblingThenConfirms = new ActOnPromptThenAnswer(
            () => sibling = f.StartSiblingAsync().GetAwaiter().GetResult(), "yes");

        var report = await RunAsync(f, Options(f) with
        {
            Yes = false,
            ConfirmationInput = startsSiblingThenConfirms
        });

        startsSiblingThenConfirms.Acted.Should().BeTrue();
        report.Code.Should().Be(ExitCode.GuardRefusal);
        report.Headline.Should().Contain("last check before the swap").And.Contain(sibling);

        f.RootEntries().Should().Equal(before, "nothing was swapped and no backup was taken");
        (await f.ScratchContainersAsync()).Should().BeEmpty("the scratch store is removed on the way out");
        (await f.StartedAtAsync()).Should().Be(startedAt, "the container is never stopped");
        (await f.ReadAsync("Order-1")).Should().HaveCount(3);
    }

    [Fact]
    public async Task rollback_refuses_while_a_sibling_container_is_running()
    {
        // A rollback discards every write made since the backup, so it is MORE destructive than a rewrite —
        // and it was the one path with no sibling and no connected-client check at all.
        await using var f = await RewriteFixture.StartAsync(output);
        (await RewriteAsync(f, Options(f) with { Eval = "dropStream(/^Junk-/)" })).Code.Should().Be(ExitCode.Ok);
        (await f.StreamExistsAsync("Junk-1")).Should().BeFalse();

        var sibling = await f.StartSiblingAsync();
        var report = await RunAsync(f, Options(f) with { Mode = RewriteMode.Rollback });

        report.Code.Should().Be(ExitCode.GuardRefusal);
        report.Headline.Should().Contain(sibling);
        (await f.StreamExistsAsync("Junk-1")).Should().BeFalse(
            "the rollback did not happen, so the rewritten store is still the live one");
        f.RootEntries().Should().NotContain(e => e.StartsWith("data.rolledback."));
    }

    // ================================================================= the confirmation prompt

    /// <summary>
    /// The only human interlock in the tool, and the one branch that most needs to work: saying anything but
    /// "yes" must leave everything exactly as it was.
    /// </summary>
    [Fact]
    public async Task Declining_the_confirmation_changes_nothing()
    {
        await using var f = await RewriteFixture.StartAsync(output);
        await f.WaitUntilNoClientsAsync(TimeSpan.FromSeconds(30));
        var before = f.RootEntries();
        var startedAt = await f.StartedAtAsync();
        var plan = new StringWriter();

        var report = await RunAsync(f, Options(f) with
        {
            Yes = false,
            Eval = "dropStream(/^Junk-/)",
            ConfirmationInput = new StringReader("no\n"),
            Output = plan
        });

        report.Code.Should().Be(ExitCode.GuardRefusal);
        f.RootEntries().Should().Equal(before, "not even the new-store directory is created");
        (await f.ScratchContainersAsync()).Should().BeEmpty();
        (await f.StartedAtAsync()).Should().Be(startedAt, "the container is never stopped");
        (await f.ReadAsync("Junk-1")).Should().HaveCount(2, "the stream the rule would have dropped is still there");

        // The prompt is also the only place the operator sees what the script was understood to MEAN.
        var printed = plan.ToString();
        printed.Should().Contain("DropStreamPredicate", "the rules must be shown before they are applied");
        printed.Should().Contain(Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes("dropStream(/^Junk-/)"))),
            "and the checksum of the exact script text");
    }

    [Fact]
    public async Task Confirming_with_yes_proceeds()
    {
        // The control: without this, "declining changes nothing" would also pass if the prompt rejected
        // every answer, or if the tool never got past the prompt at all.
        await using var f = await RewriteFixture.StartAsync(output);
        await f.WaitUntilNoClientsAsync(TimeSpan.FromSeconds(30));

        var report = await RunAsync(f, Options(f) with
        {
            Yes = false,
            Eval = "dropStream(/^Junk-/)",
            ConfirmationInput = new StringReader("yes\n")
        });

        report.Code.Should().Be(ExitCode.Ok);
        (await f.StreamExistsAsync("Junk-1")).Should().BeFalse();
    }

    // ================================================================= --no-projection-copy

    [Fact]
    public async Task no_projection_copy_suppresses_the_projection_copy_and_still_copies_the_events()
    {
        await using var f = await RewriteFixture.StartAsync(output);
        await f.WaitUntilNoClientsAsync(TimeSpan.FromSeconds(30));

        var report = await RunAsync(f, Options(f) with { NoProjectionCopy = true });

        report.Code.Should().Be(ExitCode.Ok);
        report.CopiedProjections.Should().BeEmpty();
        (await f.ProjectionStatusAsync(RewriteFixture.MergeStream)).Should().BeNull(
            "with the copy suppressed the app's projection is not pre-created — the app recreates it on boot");
        (await f.ReadAsync("Order-1")).Should().HaveCount(3, "the events are copied either way");
    }

    // ================================================================= a second run

    /// <summary>
    /// The positive anchor for every "no scratch container was left / started" assertion in this file. Those
    /// are all negatives over a searchable space, and a query that can never see anything satisfies them for
    /// free — proven: the reviewer replaced the filter with one that cannot match and they all stayed green.
    /// </summary>
    [Fact]
    public async Task The_scratch_container_query_sees_the_scratch_container_while_it_is_up()
    {
        await using var f = await RewriteFixture.StartAsync(output);
        await f.WaitUntilNoClientsAsync(TimeSpan.FromSeconds(30));

        IReadOnlyList<string> seenDuringRun = [];
        // Look while the tool is blocked on the prompt: the scratch store is not up yet at that point, so the
        // observation is taken from a background poll that runs across the whole rewrite instead.
        using var polling = new CancellationTokenSource();
        var watcher = Task.Run(async () =>
        {
            while (!polling.IsCancellationRequested)
            {
                var now = await f.ScratchContainersAsync();
                if (now.Count > 0) { seenDuringRun = now; return; }
                await Task.Delay(200);
            }
        });

        var report = await RunAsync(f, Options(f));
        await polling.CancelAsync();
        await watcher;

        report.Code.Should().Be(ExitCode.Ok);
        seenDuringRun.Should().NotBeEmpty(
            "if this query cannot see a scratch container that certainly existed, every 'no scratch container' "
            + "assertion in this file is unfalsifiable");
        seenDuringRun.Should().AllSatisfy(n => n.Should().StartWith($"mp-rewrite-{f.ContainerName}-"));
        (await f.ScratchContainersAsync()).Should().BeEmpty("and it is gone once the run finishes");
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

    [Fact]
    public async Task A_second_run_appends_its_own_history_record()
    {
        // Lead decision 4's entire purpose: a second rewrite is a new migration, not one silently skipped as
        // already applied. Two runs of the SAME script inside one second used to collide on the id.
        await using var f = await RewriteFixture.StartAsync(output);

        (await RewriteAsync(f, Options(f))).Code.Should().Be(ExitCode.Ok);
        var second = await RewriteAsync(f, Options(f));
        second.Code.Should().Be(ExitCode.Ok);

        var history = await f.ReadAsync("mp-migrations");
        history.Should().HaveCount(2, "the first run's record is carried forward and the second adds its own");
        var ids = history
            .Select(e => JsonSerializer.Deserialize<MigrationApplied>(e.Data.Span)!.Id)
            .ToList();
        ids.Should().OnlyHaveUniqueItems("two runs must not share an id — the second would be skipped");
        ids.Should().AllSatisfy(id => id.Should().StartWith("rewrite_"));
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
    public async Task Naming_both_a_script_file_and_an_inline_script_exits_2()
    {
        // One invalid command line, one exit code. The parser used to reject this too, and returned 1 —
        // which meant the code an operator's script branched on depended on which check ran first.
        await using var f = await RewriteFixture.StartAsync(output);

        var report = await RunAsync(f, Options(f) with { Eval = "dropStream(\"a\")", ScriptPath = "/tmp/x.js" });

        report.Code.Should().Be(ExitCode.ScriptError);
        report.Headline.Should().Contain("mutually exclusive");
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
