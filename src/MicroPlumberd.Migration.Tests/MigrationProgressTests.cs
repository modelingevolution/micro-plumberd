using FluentAssertions;
using KurrentDB.Client;
using MicroPlumberd.Migration;
using MicroPlumberd.Testing;
using MicroPlumberd.Tests.App.Domain;
using Xunit;
using Xunit.Abstractions;

namespace MicroPlumberd.Migration.Tests;

/// <summary>
/// Integration test for the live status + progress API (<see cref="MigrationProgressTracker"/> exposed via
/// <see cref="MigrationRunner.Progress"/>). Runs a real migration over real KurrentDB and asserts the status
/// snapshot transitions Pending → Running → Completed, that per-migration items transition
/// Pending → Running → Completed, and that per-phase progress advances and reaches 100%.
/// </summary>
[Trait("Category", "Integration")]
public class MigrationProgressTests(ITestOutputHelper output)
{
    private sealed class Server : IAsyncDisposable
    {
        public required EventStoreServer Es { get; init; }
        public required KurrentDBClient Client { get; init; }
        public required IPlumber Plumber { get; init; }

        public static async Task<Server> StartAsync(string tag)
        {
            var es = EventStoreServer.Create($"mp-prog-{tag}-{Guid.NewGuid():N}");
            await es.StartInDocker(inMemory: true);
            var s = es.GetEventStoreSettings();
            return new Server
            {
                Es = es,
                Client = new KurrentDBClient(s),
                Plumber = global::MicroPlumberd.Plumber.Create(s)
            };
        }

        public async ValueTask DisposeAsync() => await Es.DisposeAsync();
    }

    /// <summary>A pending migration that drops one unused stream — enough to exercise item state transitions.</summary>
    private sealed class NoopDrop : Migration
    {
        public override string Id => "0001_progress_probe";
        public override void Migrate(IMigrationBuilder b) => b.DropStream("Nonexistent-stream-xyz");
    }

    [Fact]
    public async Task Status_snapshot_transitions_pending_running_completed_and_progress_reaches_100()
    {
        await using var src = await Server.StartAsync("src");
        await using var dst = await Server.StartAsync("dst");

        // Seed a batch of aggregate events so the event-copy phase has real work + a non-trivial position.
        const int n = 40;
        for (var i = 0; i < n; i++)
            await src.Plumber.SaveNew(FooAggregate.Open($"foo-{i}", Guid.NewGuid()));

        var runner = new MigrationRunner();

        // BEFORE the run: the snapshot is Pending / NotStarted.
        runner.CurrentStatus.RunState.Should().Be(MigrationRunState.Pending);
        runner.CurrentStatus.Phase.Should().Be(MigrationPhase.NotStarted);

        // Capture every pushed snapshot (published synchronously on the running task; guard with a lock anyway).
        var snapshots = new List<MigrationStatusSnapshot>();
        var gate = new object();
        runner.Progress.ProgressChanged += (_, s) => { lock (gate) snapshots.Add(s); };

        // SIMPLE migration (no projection pre-copy — the app regenerates merge streams on boot via fromAll).
        var result = await runner.RunAsync(src.Client, dst.Client, [new NoopDrop()], dryRun: false);

        List<MigrationStatusSnapshot> seen;
        lock (gate) seen = snapshots.ToList();
        output.WriteLine("RunState sequence: " + string.Join(" → ", seen.Select(s => s.RunState).Distinct()));
        output.WriteLine($"final EventCopy: copied={seen[^1].EventCopy?.EventsCopied} head-based %={seen[^1].EventCopy?.PercentComplete:0.0}");

        // (1) Overall run state: Pending (before) → Running (during) → Completed (after).
        runner.CurrentStatus.RunState.Should().Be(MigrationRunState.Completed);
        runner.CurrentStatus.Phase.Should().Be(MigrationPhase.Completed);
        runner.CurrentStatus.FinishedUtc.Should().NotBeNull();
        seen.Should().Contain(s => s.RunState == MigrationRunState.Running, "the run must be observed Running mid-flight");
        seen[^1].RunState.Should().Be(MigrationRunState.Completed, "the last pushed snapshot is Completed");

        // (2) The pending migration item transitions Pending → Running → Completed.
        seen.Should().Contain(s => s.Migrations.Any(m => m.Id == "0001_progress_probe" && m.State == MigrationItemState.Running),
            "the migration must be observed Running");
        var finalItem = runner.CurrentStatus.Migrations.Single(m => m.Id == "0001_progress_probe");
        finalItem.State.Should().Be(MigrationItemState.Completed);
        finalItem.StartedUtc.Should().NotBeNull();
        finalItem.FinishedUtc.Should().NotBeNull();

        // (3) Event-copy progress advanced from 0 and reached ~100% (position-based) with the full written count.
        var ecStates = seen.Where(s => s.EventCopy is not null).Select(s => s.EventCopy!).ToList();
        ecStates.Should().NotBeEmpty();
        ecStates.Max(e => e.EventsCopied).Should().BeGreaterThan(0, "events were copied");
        var finalEc = runner.CurrentStatus.EventCopy!;
        finalEc.EventsCopied.Should().Be(result.Copy.Kept, "final copied count matches the copy engine");
        finalEc.HeadCommitPosition.Should().BeGreaterThan(0);
        finalEc.PercentComplete.Should().BeGreaterThanOrEqualTo(99.9d, "the copy reached the $all head");
    }
}
