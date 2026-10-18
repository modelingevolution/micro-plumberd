using FluentAssertions;
using KurrentDB.Client;
using MicroPlumberd.Testing;
using MicroPlumberd.Tests.Utils;
using MicroPlumberd.Services;
using MicroPlumberd.Services.ProcessManagers;
using MicroPlumberd.Tests.App.Domain;
using MicroPlumberd.Tests.App.Srv;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MicroPlumberd.Tests.Integration.Services;

/// <summary>
/// Issue #27, the sibling of #25: a process manager is loaded with <c>plumber.Rehydrate(manager, "{Name}-{id}")</c> — exactly
/// what <c>ProcessManagerClient.GetManager</c> does — not with <c>Get</c>, so #26 never reached it. Rehydrate counted DELIVERED
/// events; an event of a type this build does not map (a newer release's, after a rollback) was skipped for state AND for
/// version. The version is the STREAM REVISION of the last event read, unknown events included — the #26 rule.
/// Driven through a real process manager (XooProcessManager), never a hand-built model.
/// </summary>
[TestCategory("Integration")]
public class ProcessManagerVersionTests : IClassFixture<EventStoreServer>
{
    private readonly IPlumber _plumber;
    private readonly EventStoreServer _es;

    public ProcessManagerVersionTests(EventStoreServer es)
    {
        _plumber = Plumber.Create(es.GetEventStoreSettings());
        _es = es;
    }

    /// <summary>The stream GetManager reads, by the same literal.</summary>
    private static string ManagerStream(Guid id) => $"{nameof(XooProcessManager)}-{id}";

    private async Task RawAppend(string stream, StreamState state, string eventType)
    {
        await using var client = new KurrentDBClient(_es.GetEventStoreSettings());
        await client.AppendToStreamAsync(stream, state,
            [new EventData(Uuid.NewUuid(), eventType, System.Text.Encoding.UTF8.GetBytes("""{"Whatever":1}"""))]);
    }

    /// <summary>Loaded as GetManager loads it.</summary>
    private async Task<XooProcessManager> Load(Guid id)
    {
        var manager = new XooProcessManager();
        if (manager is IIdAware aware) aware.Id = id;   // as GetManager sets it
        await _plumber.Rehydrate(manager, ManagerStream(id));
        return manager;
    }

    /// <summary>
    /// The issue's shape: a known event, then one of a type this build does not know. The version is 1 (two events), and a
    /// write at that expected version — what a caller of GetManager does with Version — succeeds. Master: 0, WrongExpectedVersion.
    /// </summary>
    [Fact]
    public async Task AnUnknownEventInTheManagersStream_CountsForVersion_AndAWriteAtThatVersionSucceeds()
    {
        await _es.StartInDocker();
        var id = Guid.NewGuid();
        await _plumber.AppendEvents(ManagerStream(id), StreamState.NoStream, new FooCreated { Name = "known" });
        await RawAppend(ManagerStream(id), StreamState.StreamExists, "EventFromANewerRelease");

        var manager = await Load(id);

        manager.Version.Should().Be(1, "two events in the stream — the unknown one counts for version");
        var write = () => _plumber.AppendEvents(ManagerStream(id), (ulong)manager.Version, new FooCreated { Name = "next" });
        await write.Should().NotThrowAsync<WrongExpectedVersionException>();
    }

    /// <summary>
    /// A manager whose stream holds ONLY events this build does not know EXISTS: version 0, not -1. The executor reads
    /// `Version &lt; 0` as "never created" and silently drops the event that reached it.
    /// </summary>
    [Fact]
    public async Task AStreamOfOnlyUnknownEvents_IsAManagerThatExists()
    {
        await _es.StartInDocker();
        var id = Guid.NewGuid();
        await RawAppend(ManagerStream(id), StreamState.NoStream, "EventFromANewerRelease");

        (await Load(id)).Version.Should().Be(0, "the stream exists: its one event is revision 0");
    }

    /// <summary>
    /// THE PLAUSIBLE WRONG FIX's row: "count the unknown events too" (Increase() for every event) agrees with the revision only
    /// while the stream is read whole from revision 0. With its head truncated (TruncateBefore = 2, three events written), the
    /// first event read is revision 2: the revision is 2, a count is 0 — and a write at a counted version fails.
    /// </summary>
    [Fact]
    public async Task AManagersStreamWithItsHeadTruncated_HasTheRevisionAsVersion_NotACount()
    {
        await _es.StartInDocker();
        var id = Guid.NewGuid();
        await _plumber.AppendEvents(ManagerStream(id), StreamState.NoStream,
            new FooCreated { Name = "a" }, new FooCreated { Name = "b" }, new FooCreated { Name = "c" });
        await using (var client = new KurrentDBClient(_es.GetEventStoreSettings()))
            await client.SetStreamMetadataAsync(ManagerStream(id), StreamState.Any, new StreamMetadata { TruncateBefore = StreamPosition.FromInt64(2) });

        var manager = await Load(id);

        manager.Version.Should().Be(2, "the version is the revision of the last event read, not how many were read");
        var write = () => _plumber.AppendEvents(ManagerStream(id), (ulong)manager.Version, new FooCreated { Name = "d" });
        await write.Should().NotThrowAsync<WrongExpectedVersionException>();
    }

    // ---- rev-reach cc224bd49: the version is the revision, but "created" is an APPLIED event — the executor must not act ----

    /// <summary>A manager whose stream holds only unknown events has a version — and was NOT created by this build.</summary>
    [Fact]
    public async Task AStreamOfOnlyUnknownEvents_HasAVersion_ButIsNotCreated()
    {
        await _es.StartInDocker();
        var id = Guid.NewGuid();
        await RawAppend(ManagerStream(id), StreamState.NoStream, "EventFromANewerRelease");

        var manager = await Load(id);

        manager.Version.Should().Be(0);
        manager.Created.Should().BeFalse("no event of this build's was applied: it is in its default state");
    }

    [Fact]
    public async Task AManagerWithAnAppliedEvent_IsCreated()
    {
        await _es.StartInDocker();
        var id = Guid.NewGuid();
        await _plumber.AppendEvents(ManagerStream(id), StreamState.NoStream, new FooCreated { Name = "known" });
        await RawAppend(ManagerStream(id), StreamState.StreamExists, "EventFromANewerRelease");

        (await Load(id)).Created.Should().BeTrue();
    }

    /// <summary>
    /// THE EXECUTOR: a newer release created the manager through a start-event type this build cannot map (the rollback #27 is
    /// about), so this build sees only unknown events. An event reaching that manager is NOT handled — When() on its default
    /// state would enqueue a command from nothing. The lookup maps the recipient to the manager as the real projection would
    /// (a link resolving to a CommandEnqueued in a stream whose id segment is the manager's). Gated on `Version &lt; 0` (this
    /// PR's first form), the version 0 read as "created" and the executor acted: the manager's stream grew.
    /// </summary>
    [Fact]
    public async Task AnEventReachingAManagerOfOnlyUnknownEvents_IsNotHandled()
    {
        await _es.StartInDocker();
        var managerId = Guid.NewGuid();
        var recipient = Guid.NewGuid();
        await RawAppend(ManagerStream(managerId), StreamState.NoStream, "StartEventFromANewerRelease");

        // The lookup: XooProcessManagerLookup-{recipient} → a link to a CommandEnqueued whose source stream's id is the manager's.
        var source = $"LookupSource-{managerId}";
        // CreateLoo: the lookup maps the manager's CommandTypes — the commands its When returns (CommandEnqueued<CreateLoo>), not
        // StartWhen's CreateBoo (that first form resolved nowhere; the precondition below caught it).
        await _plumber.AppendEvents(source, StreamState.NoStream, CommandEnqueued.Create(recipient, new CreateLoo()));
        await using (var client = new KurrentDBClient(_es.GetEventStoreSettings()))
            await client.AppendToStreamAsync($"{nameof(XooProcessManager)}Lookup-{recipient}", StreamState.NoStream,
                [new EventData(Uuid.NewUuid(), "$>", System.Text.Encoding.UTF8.GetBytes($"0@{source}"))]);

        var services = new ServiceCollection().BuildServiceProvider();
        var pmClient = new ProcessManagerClient(services, _plumber, null!);
        var executor = new ProcessManagerExecutor<XooProcessManager>(pmClient, NullLogger<ProcessManagerExecutor<XooProcessManager>>.Instance);

        // PRECONDITION (non-vacuity): the executor will load THIS manager — the lookup resolves the recipient to it, and it is the
        // only-unknown manager (a version, nothing applied). Without this, a lookup that resolves nowhere gives a fresh, empty
        // manager (Version -1) that both gates skip, and the row passes whatever the gate is.
        var resolved = await pmClient.GetManager<XooProcessManager>(recipient);
        resolved.Id.Should().Be(managerId, "the lookup must resolve the recipient to the manager under test");
        resolved.Version.Should().Be(0);
        resolved.Created.Should().BeFalse();
        var reaching = new Metadata(recipient, Guid.NewGuid(), 0, null, $"Boo-{recipient}", default);

        await executor.Handle(reaching, new BooRefined { Name = "x" });

        await using var reader = new KurrentDBClient(_es.GetEventStoreSettings());
        (await reader.ReadStreamAsync(Direction.Forwards, ManagerStream(managerId), StreamPosition.Start).ToListAsync())
            .Should().ContainSingle("nothing was linked or enqueued: the manager was never created by this build");
    }

    /// <summary>A HAND-WRITTEN manager that is IVersionAware but does not count applied events — not ProcessManagerBase.</summary>
    public sealed class VersionOnlyManager : IProcessManager, IVersionAware, ITypeRegister
    {
        public long Version { get; set; } = -1;
        public object Id => Guid.Empty;
        public static Type StartEvent => typeof(FooCreated);
        public static IEnumerable<Type> CommandTypes => [];
        public static IEnumerable<Type> Types => [typeof(FooCreated)];
        public Task Handle(Metadata m, object ev) => Task.CompletedTask;
        public Task<ICommandRequest?> HandleError(ExecutionContext executionContext) => Task.FromResult<ICommandRequest?>(null);
        public Task<ICommandRequest?> When(Metadata m, object evt) => Task.FromResult<ICommandRequest?>(null);
        public Task<ICommandRequest> StartWhen(Metadata m, object evt) => throw new NotSupportedException();
    }

    /// <summary>
    /// rev-reach (CB2): for an IVersionAware manager WITHOUT the applied-events count, `Version >= 0` is the flipped gate (an
    /// only-unknown stream reads as created). It cannot be told apart, so the executor REFUSES it when built — named. The generated
    /// XooProcessManager (ProcessManagerBase) is built without complaint (the executor row above).
    /// </summary>
    [Fact]
    public void AnIVersionAwareManagerThatDoesNotCountAppliedEvents_IsRefused_ByName()
    {
        var build = () => new ProcessManagerExecutor<VersionOnlyManager>(
            new ProcessManagerClient(new ServiceCollection().BuildServiceProvider(), _plumber, null!),
            NullLogger<ProcessManagerExecutor<VersionOnlyManager>>.Instance);

        build.Should().Throw<InvalidOperationException>().WithMessage($"*{typeof(VersionOnlyManager).FullName}*IVersionAware but not IAppliedEventsAware*");
    }
}
