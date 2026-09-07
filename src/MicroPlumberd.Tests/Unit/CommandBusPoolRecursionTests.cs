using FluentAssertions;
using KurrentDB.Client;
using MicroPlumberd.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MicroPlumberd.Tests.Unit;

/// <summary>
/// Resolution-safety tests for the SCOPED command-bus registration
/// (<c>AddPlumberd(…, scopedCommandBus: true)</c>).
///
/// <para>Regression guard for a hang observed in rocket-welder2 on MicroPlumberd 1.2.5.0: every request
/// that touched the device-command path never returned. A managed stack capture showed an unbounded
/// repeating cycle:</para>
/// <code>
/// GetService(ICommandBus)                       // public, scoped
///   → Scrutor decorator  → GetRequiredKeyedService(ICommandBus, key)
///     → AddPlumberd's ICommandBus factory
///       → CommandBusPool.Init()
///         → CommandBusPoolScoped.Create()
///           → GetRequiredService&lt;ICommandBus&gt;()   // PUBLIC again — back to the top
/// </code>
///
/// <para>The pool built itself by resolving the very service whose factory builds the pool. Before
/// 1.2.5.0 that cycle terminated by accident: <c>Init()</c> assigned <c>_semaphore</c> BEFORE calling
/// <c>Create()</c>, so the nested <c>Init()</c> saw a non-null semaphore and returned early. The 1.2.5.0
/// race fix (build under a gate, publish <c>_semaphore</c> LAST) legitimately closed a null-<c>_pool</c>
/// window — and in doing so removed the accidental recursion breaker, because <c>lock</c> is re-entrant
/// on the same thread.</para>
///
/// <para>The fix removes the cycle itself: the pool no longer resolves the public, decorated
/// <c>ICommandBus</c>. See <see cref="CommandBusPoolScoped"/>.</para>
/// </summary>
public class CommandBusPoolRecursionTests
{
    /// <summary>Bounds the assertion: the defect is unbounded recursion, which manifests as a hang.</summary>
    private static readonly TimeSpan ResolveBudget = TimeSpan.FromSeconds(20);

    private static ServiceProvider BuildHost(int poolSize = 4)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // Exactly how rocket-welder2 wires it. No connection is made by resolving — only construction.
        services.AddPlumberd(
            _ => KurrentDBClientSettings.Create("esdb://admin:changeit@127.0.0.1:2113?tls=false"),
            scopedCommandBus: true,
            commandBusPoolSize: poolSize);
        return services.BuildServiceProvider();
    }

    // THE test that could not exist before the fix. RED on 1.2.5.0: this never completes.
    [Fact]
    public async Task Resolving_scoped_ICommandBus_completes()
    {
        await using var sp = BuildHost();
        using var scope = sp.CreateScope();

        var resolve = Task.Run(() => scope.ServiceProvider.GetRequiredService<ICommandBus>());

        var finished = await Task.WhenAny(resolve, Task.Delay(ResolveBudget));

        finished.Should().BeSameAs(resolve,
            "resolving the scoped ICommandBus must not re-enter its own factory; on 1.2.5.0 the pool " +
            "built itself by resolving the public ICommandBus, so this recursed until the app hung");
        (await resolve).Should().NotBeNull();
    }

    // The pool must still end up initialised and rentable — a recursion fix that simply skips Init()
    // would pass the test above and then fail every QueueAsync with "has not been initialised".
    [Fact]
    public async Task Resolving_scoped_ICommandBus_initialises_a_rentable_pool()
    {
        await using var sp = BuildHost();
        using var scope = sp.CreateScope();

        var resolve = Task.Run(() => scope.ServiceProvider.GetRequiredService<ICommandBus>());
        (await Task.WhenAny(resolve, Task.Delay(ResolveBudget))).Should().BeSameAs(resolve);
        await resolve;

        var pool = sp.GetRequiredService<ICommandBusPool>();

        var rent = pool.RentScope().AsTask();
        (await Task.WhenAny(rent, Task.Delay(ResolveBudget))).Should().BeSameAs(rent,
            "Init() must have run to completion during the resolve above");
        using var owner = await rent;
        owner.Should().NotBeNull();
    }

    // Concurrency guard: the 1.2.5.0 race fix must survive. Several request threads resolving the
    // scoped bus at once must all complete, and the pool must be initialised exactly once.
    [Fact]
    public async Task Concurrent_scoped_resolves_all_complete()
    {
        await using var sp = BuildHost(poolSize: 8);

        var resolves = Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
        {
            using var scope = sp.CreateScope();
            return scope.ServiceProvider.GetRequiredService<ICommandBus>();
        })).ToArray();

        var all = Task.WhenAll(resolves);
        (await Task.WhenAny(all, Task.Delay(ResolveBudget))).Should().BeSameAs(all,
            "concurrent first-resolves must not deadlock or recurse");
        (await all).Should().OnlyContain(x => x != null);
    }

    // ── The scoped semantics themselves (owner ruling, 2026-08-26) ──────────────────────────────

    /// <summary>
    /// <c>ICommandBus</c> in rocket-welder2 <b>must be scoped</b> — an owner architectural requirement,
    /// not a preference. A scope is an authenticated circuit, so a scoped bus is the seam that stamps a
    /// command with the identity of the operator who issued it. A singleton bus attributes every command
    /// in the plant to one identity: invisible while auth is off, silently wrong the day it is turned on,
    /// and unanswerable exactly when it matters — "who stopped the cell?".
    /// </summary>
    /// <remarks>
    /// Pinned here so no future simplification quietly de-scopes it. The failure this guards against is
    /// not a crash: flipping the registration to singleton passes every other test in this file and
    /// changes nothing observable until the day attribution is needed.
    /// </remarks>
    [Fact]
    public async Task Scoped_registration_yields_one_bus_per_scope()
    {
        await using var sp = BuildHost();

        using var scopeA = sp.CreateScope();
        var a1 = scopeA.ServiceProvider.GetRequiredService<ICommandBus>();
        var a2 = scopeA.ServiceProvider.GetRequiredService<ICommandBus>();

        using var scopeB = sp.CreateScope();
        var b1 = scopeB.ServiceProvider.GetRequiredService<ICommandBus>();

        a2.Should().BeSameAs(a1, "twice in ONE scope is one bus — that is what makes the scope the unit of attribution");
        b1.Should().NotBeSameAs(a1, "a second scope is a second circuit, and must get its own bus");
    }

    /// <summary>
    /// The complement, and the reason the pool exists at all: singletons have no user context of their
    /// own, so they rent from <see cref="ICommandBusPool"/> rather than holding a scoped bus. The pool is
    /// a singleton and its rentals are not the scoped instances.
    /// </summary>
    [Fact]
    public async Task The_pool_is_the_singleton_complement_not_the_scoped_bus()
    {
        await using var sp = BuildHost();

        sp.GetRequiredService<ICommandBusPool>()
          .Should().BeSameAs(sp.GetRequiredService<ICommandBusPool>(), "the pool is the singleton half of the pair");

        using var scope = sp.CreateScope();
        var scoped = scope.ServiceProvider.GetRequiredService<ICommandBus>();

        using var rented = await sp.GetRequiredService<ICommandBusPool>().RentScope();
        rented.Should().NotBeSameAs(scoped, "a rented bus serves a caller that has no scope of its own");
    }
}
