using System.Text;
using FluentAssertions;
using KurrentDB.Client;
using MicroPlumberd.Testing;
using MicroPlumberd.Tests.App.Domain;
using MicroPlumberd.Tests.App.Infrastructure;
using MicroPlumberd.Tests.Utils;

namespace MicroPlumberd.Tests.Integration;

/// <summary>
/// A projection create the store half-recorded (seen on a station: a first start under a swapping host timed out). KurrentDB
/// wrote the name to <c>$projections-$all</c> but not the definition. Until the projection subsystem restarts, EVERY create
/// fails "received OperationFailed" (KurrentDB: "Created event ... has not been written to $projections-$all ... WrongExpectedVersion").
/// After it restarts, the name is a ghost (status Creating, mode Transient, not in the continuous list): every create fails
/// "received Conflict" (KurrentDB: "Duplicate projection names"), and update, abort and reset never reply. Nothing healed
/// it: 32 restarts.
/// <para>
/// The ghost is planted the way the server leaves it: only the <c>$ProjectionCreated</c> record is appended.
/// </para>
/// </summary>
[TestCategory("Integration")]
public class HalfCreatedProjectionTests : IAsyncDisposable, IDisposable
{
    private readonly EventStoreServer _eventStore = new();
    private readonly IPlumber plumber;

    public HalfCreatedProjectionTests() => plumber = Plumber.Create(_eventStore.GetEventStoreSettings());

    public async ValueTask DisposeAsync() => await _eventStore.DisposeAsync();
    public void Dispose() => _eventStore.Dispose();

    private string ModelStream => plumber.Config.Conventions.OutputStreamModelConvention(typeof(FooModel));

    /// <summary>State 2: the ghost after the store (here: its projection subsystem) restarted.</summary>
    [Fact]
    public async Task AGhostLeftByAHalfRecordedCreate_IsReplaced_AndTheProjectionLinksTheHistory()
    {
        await _eventStore.StartInDocker();
        await plumber.SaveNew(FooAggregate.Open("before the failure"));
        await PlantHalfRecordedCreate(ModelStream);
        await plumber.ProjectionManagementClient.RestartSubsystemAsync();
        await WaitUntilGhost(ModelStream);

        (await plumber.TryCreateJoinProjection<FooModel>()).Should().BeTrue("the ghost is replaced by the projection");

        await ShouldBeAWorkingProjection();
    }

    /// <summary>State 1: the same half-recorded create on a store that has not restarted since.</summary>
    [Fact]
    public async Task AHalfRecordedCreateOnALiveStore_IsRecoveredWithoutAStoreRestart_AndTheProjectionLinksTheHistory()
    {
        await _eventStore.StartInDocker();
        await plumber.SaveNew(FooAggregate.Open("before the failure"));
        await PlantHalfRecordedCreate(ModelStream);

        (await plumber.TryCreateJoinProjection<FooModel>()).Should().BeTrue("the projection is created");

        await ShouldBeAWorkingProjection();
    }

    private async Task ShouldBeAWorkingProjection()
    {
        var status = await plumber.ProjectionManagementClient.GetStatusAsync(ModelStream);
        status!.Mode.Should().Be("Continuous");
        (await plumber.ProjectionManagementClient.ListContinuousAsync().CountAsync(p => p.Name == ModelStream))
            .Should().Be(1, "the continuous list, which the register reads, holds it once");

        var model = new FooModel(new InMemoryAssertionDb());
        await using (await plumber.SubscribeEventHandler(model, ensureOutputStreamProjection: false))
        {
            await plumber.SaveNew(FooAggregate.Open("after the recovery"));
            await WaitUntil(() => model.AssertionDb.Index.Count >= 2);
            model.AssertionDb.Index.Should().HaveCount(2, "the event from before the failure and the new one are linked");
        }

        (await Plumber.Create(_eventStore.GetEventStoreSettings()).TryCreateJoinProjection<FooModel>())
            .Should().BeFalse("the next start finds it unchanged");
    }

    /// <summary>What a create that timed out under load leaves: the name in <c>$projections-$all</c>, no definition.</summary>
    private async Task PlantHalfRecordedCreate(string name) =>
        await plumber.Client.AppendToStreamAsync("$projections-$all", StreamState.Any,
            [new EventData(Uuid.NewUuid(), "$ProjectionCreated", Encoding.UTF8.GetBytes(name), contentType: "application/octet-stream")]);

    private async Task WaitUntilGhost(string name) => await WaitUntil(async () =>
    {
        try { return (await plumber.ProjectionManagementClient.GetStatusAsync(name)) is { Mode: "Transient" } s && s.Status.Contains("Creating"); }
        catch (Exception) { return false; } // the subsystem is restarting
    });

    private static Task WaitUntil(Func<bool> condition) => WaitUntil(() => Task.FromResult(condition()));

    private static async Task WaitUntil(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("condition not met within 60 s");
            await Task.Delay(200);
        }
    }
}
