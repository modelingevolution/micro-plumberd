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
            .Contain($"'{ModelStream}'").And.Contain("DIFFERENT definition").And.Contain("not overwritten");
        (await ProjectionQueryHash.ReadWithSourceAsync(plumber.Client, ModelStream)).Item1.Should().Be(stored,
            "the existing definition was kept");
        (await ProjectionsNamed(ModelStream)).Should().Be(1);
    }

    /// <summary>
    /// A creator SLOWER than another's wait (the PR #34 limit, rev-reach cae59475b): the other definition's hash is already
    /// stored when this one comes to store its own. The first hash is stored CONDITIONALLY, so this one is refused, reads the
    /// stored hash, and surfaces — its late store no longer overwrites the hash of the definition that replaced it.
    /// </summary>
    [Fact]
    public async Task ACreatorWhoseHashWasStoredFirstByADifferentDefinition_Surfaces_AndDoesNotOverwriteIt()
    {
        await _eventStore.StartInDocker();
        const string theOther = "0000000000000000000000000000000000000000000000000000000000000000";
        await WithStoreReady(() => ProjectionQueryHash.StoreAsync(plumber.Client, ModelStream, theOther));

        var created = await Record.ExceptionAsync(() => plumber.TryCreateJoinProjection<FooModel>());

        created.Should().BeOfType<InvalidOperationException>().Which.Message.Should()
            .Contain($"'{ModelStream}'").And.Contain("DIFFERENT definition").And.Contain("while this one was being created");
        (await ProjectionQueryHash.ReadAsync(plumber.Client, ModelStream)).Should().Be(theOther, "the stored hash was not overwritten");
    }

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
