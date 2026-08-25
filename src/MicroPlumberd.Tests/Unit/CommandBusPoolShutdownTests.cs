using FluentAssertions;
using MicroPlumberd.Services;
using NSubstitute;
using Xunit;

namespace MicroPlumberd.Tests.Unit;

/// <summary>
/// Teardown-safety tests for <see cref="CommandBusPool"/>.
///
/// <para>These are regression guards for a host-shutdown failure observed in rocket-welder2: every test
/// body in <c>StreamingRelayIntegrationTests</c> passed, and <c>WebApplicationFactory.DisposeAsync</c>
/// then threw <c>NullReferenceException</c> out of
/// <c>ServiceProviderEngineScope.DisposeAsync → CommandBusPool.DisposeAsync</c>, failing whichever test
/// happened to own that host. The suite failed 2–6 tests out of 6, varying run to run on identical
/// code.</para>
///
/// <para>The cause is the SCOPED registration path. <c>AddPlumberd(…, scopedCommandBus: true)</c>
/// registers <see cref="CommandBusPool"/> WITHOUT calling <c>Init()</c> — it has to, because
/// <c>Create()</c> resolves <c>ICommandBus</c>, whose factory needs the pool, so eager initialisation is
/// circular. <c>Init()</c> is therefore deferred to the first resolve of a scoped <c>ICommandBus</c>.
/// A host that is built and torn down without anything ever sending a command consequently disposes a
/// pool whose backing fields are still null. Whether a given test sent a command is exactly the
/// coin-flip that produced the varying count.</para>
/// </summary>
public class CommandBusPoolShutdownTests
{
    // RED without the fix: DisposeAsync dereferenced _semaphore and _pool unconditionally, so an
    // uninitialised pool threw NullReferenceException straight out of the DI container's disposal.
    // An uninitialised pool is a LEGAL state — there is simply nothing to release.
    [Fact]
    public async Task DisposeAsync_WithoutInit_DoesNotThrow()
    {
        var pool = new CommandBusPool(Substitute.For<IServiceProvider>(), maxCount: 4);

        var dispose = async () => await pool.DisposeAsync();

        await dispose.Should().NotThrowAsync(
            "a host can be built and torn down without ever renting a command bus, so Init() never ran");
    }

    [Fact]
    public async Task DisposeAsync_WithoutInit_CalledTwice_DoesNotThrow()
    {
        var pool = new CommandBusPool(Substitute.For<IServiceProvider>(), maxCount: 4);
        await pool.DisposeAsync();

        var second = async () => await pool.DisposeAsync();

        await second.Should().NotThrowAsync("DisposeAsync must be idempotent");
    }

    // Guards the second defect fixed in the same class: Init() published _semaphore BEFORE building
    // _pool, so a concurrent caller saw a non-null semaphore, returned early from the fast path, and
    // then hit a null _pool inside RentScope. _pool is now published first, under a gate.
    [Fact]
    public void Init_IsIdempotent_AndPublishesPoolBeforeSemaphore()
    {
        var pool = new CountingPool(Substitute.For<IServiceProvider>(), maxCount: 4);

        pool.Init();
        pool.Init();
        pool.Init();

        pool.CreateCalls.Should().Be(1, "Init must build the pool exactly once");
    }

    /// <summary>Counts <c>Create</c> calls and yields buses without touching the container.</summary>
    private sealed class CountingPool(IServiceProvider sp, int maxCount) : CommandBusPool(sp, maxCount)
    {
        public int CreateCalls { get; private set; }

        public override IEnumerable<ICommandBus> Create(int number)
        {
            CreateCalls++;
            for (var i = 0; i < number; ++i)
                yield return Substitute.For<ICommandBus>();
        }
    }
}
