using Grpc.Core;
using FluentAssertions;
using KurrentDB.Client;
using MicroPlumberd.Testing;
using MicroPlumberd.Tests.App.Domain;
using MicroPlumberd.Tests.App.Infrastructure;
using MicroPlumberd.Tests.Utils;

namespace MicroPlumberd.Tests.Integration;

/// <summary>
/// Issue #29: TryCreateJoinProjection met a projection created earlier in the same process and failed "Envelope callback
/// expected Updated, received Conflict". The register is loaded ONCE per process, so it does not see a projection created
/// after it loaded: a second ensure of the same model took the create path. An IDENTICAL definition is success; a
/// DIFFERENT one surfaces, named, and is never overwritten.
/// </summary>
[TestCategory("Integration")]
public class JoinProjectionConflictTests : IAsyncDisposable, IDisposable
{
    private readonly EventStoreServer _eventStore = new();
    private readonly IPlumber plumber;

    public JoinProjectionConflictTests() => plumber = Plumber.Create(_eventStore.GetEventStoreSettings());

    // Both, as the suite's other store-owning classes: whichever the runner calls, the store is removed (a run of this class
    // with only IAsyncDisposable left its stores behind).
    public async ValueTask DisposeAsync() => await _eventStore.DisposeAsync();
    public void Dispose() => _eventStore.Dispose();

    private string ModelStream => plumber.Config.Conventions.OutputStreamModelConvention(typeof(FooModel));

    private async Task<int> ProjectionsNamed(string name) =>
        await plumber.ProjectionManagementClient.ListContinuousAsync().CountAsync(p => p.Name == name);

    /// <summary>The reproduction: create the projection, then subscribe AT ONCE with the default ensure.</summary>
    [Fact]
    public async Task CreatingAProjection_ThenSubscribingWithTheDefaultEnsure_DoesNotThrow_AndLeavesOneProjection()
    {
        await _eventStore.StartInDocker();
        await plumber.SaveNew(FooAggregate.Open("Hello"));

        (await plumber.TryCreateJoinProjection<FooModel>()).Should().BeTrue("the first ensure creates it");

        var model = new FooModel(new InMemoryAssertionDb());
        var subscribe = await Record.ExceptionAsync(async () =>
        {
            await using var sub = await plumber.SubscribeEventHandler(model);   // ensureOutputStreamProjection: true (the default)
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (model.AssertionDb.Index.Count < 1 && DateTime.UtcNow < deadline) await Task.Delay(100);
        });

        subscribe.Should().BeNull("an identical projection that already exists is success, not a Conflict");
        (await ProjectionsNamed(ModelStream)).Should().Be(1, "exactly one projection by that name");
        model.AssertionDb.Index.Should().HaveCount(1, "and the projection the model subscribed to works");
    }

    [Fact]
    public async Task ASecondIdenticalEnsure_ReportsNoChange()
    {
        await _eventStore.StartInDocker();

        (await plumber.TryCreateJoinProjection<FooModel>()).Should().BeTrue();
        (await plumber.TryCreateJoinProjection<FooModel>()).Should().BeFalse("the same definition already exists: nothing was changed");
        (await ProjectionsNamed(ModelStream)).Should().Be(1);
    }

    /// <summary>The same output stream defined DIFFERENTLY by a second code path: surfaced, named — never overwritten.</summary>
    [Fact]
    public async Task ADifferentDefinitionOfTheSameOutputStream_Surfaces_AndIsNotOverwritten()
    {
        await _eventStore.StartInDocker();
        (await plumber.TryCreateJoinProjection<FooModel>()).Should().BeTrue();
        var (stored, _) = await ProjectionQueryHash.ReadWithSourceAsync(plumber.Client, ModelStream);

        var different = await Record.ExceptionAsync(() => plumber.ProjectionManagementClient.TryCreateJoinProjection(
            plumber.Client, ModelStream, plumber.ProjectionRegister, ["SomeOtherEvent"]));

        different.Should().BeOfType<InvalidOperationException>().Which.Message.Should()
            .Contain($"'{ModelStream}'").And.Contain("DIFFERENT definition").And.Contain("not overwritten")
            .And.Contain(typeof(FooModel).FullName!, "the generic ensure names its model type")
            .And.Contain("by output stream name", "and the by-name ensure says it has none");
        (await ProjectionQueryHash.ReadWithSourceAsync(plumber.Client, ModelStream)).Item1.Should().Be(stored,
            "the existing definition was kept");
        (await ProjectionsNamed(ModelStream)).Should().Be(1);
    }

    private const string Rival = "0000000000000000000000000000000000000000000000000000000000000000";

    /// <summary>
    /// (a) THE MUST-STILL-START CASE (rev-reach e3712ab61 M1): a hash left on the hash stream by a projection that was deleted
    /// to be redefined. The create SUCCEEDS, so this process IS the creator and the hash is STALE: it is overwritten, and the
    /// app starts. 4f866b1 stored "no hash yet" conditionally, was refused, and THREW — an ensure that could never succeed,
    /// so the app could not start; its own row asserted that throw.
    /// </summary>
    [Fact]
    public async Task AStaleHashLeftByADeletedProjection_IsOverwrittenByTheCreator_AndTheEnsureSucceeds()
    {
        await _eventStore.StartInDocker();
        await WithStoreReady(() => ProjectionQueryHash.StoreAsync(plumber.Client, ModelStream, Rival));

        (await plumber.TryCreateJoinProjection<FooModel>()).Should().BeTrue("the creator overwrites a stale hash and starts");

        (await ProjectionQueryHash.ReadAsync(plumber.Client, ModelStream)).Should().NotBe(Rival, "the stale hash was replaced");
        (await plumber.TryCreateJoinProjection<FooModel>()).Should().BeFalse("and it is OURS: a second ensure sees the same definition");
    }

    /// <summary>
    /// (b) A RIVAL that stores between this creator's create and its claim (through the seam): the claim, made at the revision
    /// read before the create, is refused, and the rival's DIFFERENT definition surfaces — its hash is not overwritten.
    /// </summary>
    [Fact]
    public async Task ARivalStoringBetweenTheCreateAndTheClaim_Surfaces_AndItsHashIsKept()
    {
        await _eventStore.StartInDocker();
        KurrentDBProjectionManagementClientExtensions.BeforeHashClaimForTests = (client, stream) =>
            ReferenceEquals(client, plumber.Client) && stream == ModelStream
                ? ProjectionQueryHash.StoreAsync(client, stream, Rival) : Task.CompletedTask;
        try
        {
            var created = await Record.ExceptionAsync(() => plumber.TryCreateJoinProjection<FooModel>());

            created.Should().BeOfType<InvalidOperationException>().Which.Message.Should()
                .Contain($"'{ModelStream}'").And.Contain("DIFFERENT definition").And.Contain("while this one was being created");
            (await ProjectionQueryHash.ReadAsync(plumber.Client, ModelStream)).Should().Be(Rival);
        }
        finally { KurrentDBProjectionManagementClientExtensions.BeforeHashClaimForTests = null; }
    }

    /// <summary>
    /// m2 (rev-reach e3712ab61): the no-hash branch CLAIMS before it updates. A projection exists with no hash (the register,
    /// loaded before it existed, sends the ensure down the create path); a rival claims first → the claim is refused, the
    /// rival's definition surfaces, and THIS definition's query was never written (no update was made).
    /// </summary>
    [Fact]
    public async Task ARefusedClaim_InTheNoHashBranch_WritesNoQuery()
    {
        await _eventStore.StartInDocker();
        await WithStoreReady(async () => await plumber.ProjectionRegister.Get(ModelStream));   // the register loads: empty
        await plumber.ProjectionManagementClient.CreateContinuousAsync(ModelStream, "fromStreams(['$et-NothingAtAll']).when({})", false);

        var updates = 0;
        var wait = KurrentDBProjectionManagementClientExtensions.ConflictHashWait;
        KurrentDBProjectionManagementClientExtensions.ConflictHashWait = TimeSpan.FromMilliseconds(300);
        KurrentDBProjectionManagementClientExtensions.BeforeHashClaimForTests = (client, stream) =>
            ReferenceEquals(client, plumber.Client) && stream == ModelStream
                ? ProjectionQueryHash.StoreAsync(client, stream, Rival) : Task.CompletedTask;
        KurrentDBProjectionManagementClientExtensions.OnQueryUpdateForTests = (management, stream) =>
        {
            if (ReferenceEquals(management, plumber.ProjectionManagementClient) && stream == ModelStream) Interlocked.Increment(ref updates);
        };
        try
        {
            var ensured = await Record.ExceptionAsync(() => plumber.TryCreateJoinProjection<FooModel>());

            ensured.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Contain("DIFFERENT definition");
            updates.Should().Be(0, "a refused claim means another definition got there first: this one's query must not be written");
            (await ProjectionQueryHash.ReadAsync(plumber.Client, ModelStream)).Should().Be(Rival);
        }
        finally
        {
            KurrentDBProjectionManagementClientExtensions.BeforeHashClaimForTests = null;
            KurrentDBProjectionManagementClientExtensions.OnQueryUpdateForTests = null;
            KurrentDBProjectionManagementClientExtensions.ConflictHashWait = wait;
        }
    }

    // card microplumberd-projection-register-invalidation: the register (loaded once per process) is dropped after an ensure
    // takes the create path, so it sees the projection that now exists — the #29 root cause, at its root.

    private ProjectionRegister Register => (ProjectionRegister)plumber.ProjectionRegister;

    /// <summary>R1: the register, loaded before the projection existed, sees the projection THIS process just created.</summary>
    [Fact]
    public async Task TheRegister_SeesAProjectionThisProcessCreated_AfterItLoaded()
    {
        await _eventStore.StartInDocker();
        await WithStoreReady(async () => (await plumber.ProjectionRegister.Get(ModelStream)).Should().BeNull("loaded before it exists"));

        (await plumber.TryCreateJoinProjection<FooModel>()).Should().BeTrue();

        (await plumber.ProjectionRegister.Get(ModelStream)).Should().NotBeNull("the ensure created it; a second ensure must take the update path");
    }

    /// <summary>
    /// R2: ANOTHER instance created it after this register loaded; this ensure meets the Conflict, resolves it as identical
    /// (no change) — and the register must see it too. The plausible wrong fix, invalidating only when the ensure CHANGED
    /// something, leaves it stale here.
    /// </summary>
    [Fact]
    public async Task TheRegister_SeesAProjectionAnotherInstanceCreated_OnceAnEnsureMetIt()
    {
        await _eventStore.StartInDocker();
        await WithStoreReady(async () => (await plumber.ProjectionRegister.Get(ModelStream)).Should().BeNull());
        var other = Plumber.Create(_eventStore.GetEventStoreSettings());
        (await other.TryCreateJoinProjection<FooModel>()).Should().BeTrue();

        (await plumber.TryCreateJoinProjection<FooModel>()).Should().BeFalse("the identical projection already exists");

        (await plumber.ProjectionRegister.Get(ModelStream)).Should().NotBeNull();
    }

    /// <summary>
    /// R4, THE MUST-STILL-WORK CASE: a definition left by an EARLIER run (another instance stands in for the previous
    /// deployment) that differs from this run's — a model changed in a new deployment. It is UPDATED, not refused: the
    /// one-definition rule is per process RUN, never "whatever the store holds".
    /// </summary>
    [Fact]
    public async Task ADefinitionFromAnEarlierRun_IsUpdatedToThisRunsDefinition_NotRefused()
    {
        await _eventStore.StartInDocker();
        var earlierRun = Plumber.Create(_eventStore.GetEventStoreSettings());
        await WithStoreReady(async () => (await earlierRun.ProjectionManagementClient.TryCreateJoinProjection(
            earlierRun.Client, ModelStream, earlierRun.ProjectionRegister, ["SomeOtherEvent"])).Should().BeTrue());

        (await plumber.TryCreateJoinProjection<FooModel>()).Should().BeTrue("this run's definition replaces the earlier run's");

        (await plumber.TryCreateJoinProjection<FooModel>()).Should().BeFalse("and it is this run's one definition now");
        (await ProjectionsNamed(ModelStream)).Should().Be(1);
    }

    /// <summary>
    /// rev-reach 9499dc2cb: the one-definition refusal names BOTH definers by FULL name, so two model types whose short names
    /// collide (and so map to the same output stream) are obvious — not two identical-looking "ClashModel"s.
    /// </summary>
    [Fact]
    public async Task TheOneDefinitionRefusal_NamesBothModelTypes_ByFullName()
    {
        await _eventStore.StartInDocker();
        const string stream = "clash-row";
        await WithStoreReady(() => KurrentDBProjectionManagementClientExtensions.TryCreateJoinProjectionBy(
            plumber.ProjectionManagementClient, plumber.Client, stream, plumber.ProjectionRegister, ["ClashEventA"],
            typeof(ClashA.ClashModel)));

        var second = await Record.ExceptionAsync(() => KurrentDBProjectionManagementClientExtensions.TryCreateJoinProjectionBy(
            plumber.ProjectionManagementClient, plumber.Client, stream, plumber.ProjectionRegister, ["ClashEventB"],
            typeof(ClashB.ClashModel)));

        second.Should().BeOfType<InvalidOperationException>().Which.Message.Should()
            .Contain(typeof(ClashA.ClashModel).FullName!).And.Contain(typeof(ClashB.ClashModel).FullName!)
            .And.Contain("DIFFERENT definition");
    }

    /// <summary>R3, the cost: an ensure of a projection the register already lists does NOT reload the list (paid at every startup).</summary>
    [Fact]
    public async Task AnEnsureOfARegisteredProjection_DoesNotReloadTheRegister()
    {
        await _eventStore.StartInDocker();
        // Registered but NOT ensured in this run (an earlier run created it), so the ensure below really takes the
        // update-if-changed path — a projection this run already ensured is answered before the register is consulted.
        var earlierRun = Plumber.Create(_eventStore.GetEventStoreSettings());
        await WithStoreReady(() => earlierRun.TryCreateJoinProjection<FooModel>());
        (await plumber.ProjectionRegister.Get(ModelStream)).Should().NotBeNull();
        var loads = Register.Loads;

        (await plumber.TryCreateJoinProjection<FooModel>()).Should().BeFalse("the identical definition: no change");
        (await plumber.ProjectionRegister.Get(ModelStream)).Should().NotBeNull();

        Register.Loads.Should().Be(loads, "the update-if-changed path must not drop the list");
    }
    /// <summary>
    /// microplumberd-isconflict-status: the shape KurrentDB.Client ACTUALLY gives a create of an existing projection —
    /// StatusCode Unknown with Detail "… received Conflict", not AlreadyExists — read from a real double create, so a client
    /// upgrade that changes it reddens here instead of silently sending #29's resolution down the failure path.
    /// </summary>
    [Fact]
    public async Task ACreateOfAnExistingProjection_IsRecognisedAsAConflict_InTheShapeTheClientReallyGives()
    {
        await _eventStore.StartInDocker();
        const string query = "fromStreams(['$et-NothingAtAll']).when({})";
        await WithStoreReady(() => plumber.ProjectionManagementClient.CreateContinuousAsync("isconflict-row", query, false));

        var second = await Record.ExceptionAsync(() => plumber.ProjectionManagementClient.CreateContinuousAsync("isconflict-row", query, false));

        var rpc = second.Should().BeOfType<RpcException>().Subject;
        rpc.StatusCode.Should().Be(StatusCode.Unknown, "measured: the client does not report AlreadyExists for this");
        rpc.Status.Detail.Should().Contain(KurrentDBProjectionManagementClientExtensions.ConflictDetail);
        KurrentDBProjectionManagementClientExtensions.IsConflict(rpc).Should().BeTrue();
    }

    /// <summary>The deny path: a real refusal that is NOT a conflict (an update of a projection that does not exist).</summary>
    [Fact]
    public async Task ARefusalThatIsNotAConflict_IsNotTakenForOne()
    {
        await _eventStore.StartInDocker();
        await WithStoreReady(() => plumber.ProjectionManagementClient.CreateContinuousAsync("isconflict-ready",
            "fromStreams(['$et-NothingAtAll']).when({})", false));   // ready — so the refusal below is a single, real one

        var refused = await Record.ExceptionAsync(() =>
            plumber.ProjectionManagementClient.UpdateAsync("isconflict-no-such-projection", "fromAll().when({})", true));

        var rpc = refused.Should().BeOfType<RpcException>().Subject;
        KurrentDBProjectionManagementClientExtensions.IsConflict(rpc).Should().BeFalse($"'{rpc.Status.Detail}' ({rpc.StatusCode}) is not a conflict");
    }

    /// <summary>A client that reports the conflict as AlreadyExists, with any detail, is still recognised (kept per the card).</summary>
    [Fact]
    public void AnAlreadyExistsStatus_IsAConflict_WhateverItsDetail() =>
        KurrentDBProjectionManagementClientExtensions.IsConflict(new RpcException(new Status(StatusCode.AlreadyExists, "")))
            .Should().BeTrue();

    private static async Task WithStoreReady(Func<Task> act)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (true)
        {
            try { await act(); return; }
            catch (Exception) when (DateTime.UtcNow < deadline) { await Task.Delay(500); }
        }
    }
}
