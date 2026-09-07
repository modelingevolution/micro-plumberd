using MicroPlumberd.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MicroPlumberd.Services;

/// <summary>
/// Provides a scoped command bus pool that creates command buses within a dedicated service scope.
/// </summary>
sealed class CommandBusPoolScoped(IServiceProvider sp, int maxCount) : CommandBusPool(sp, maxCount)
{
    private readonly IServiceScope _scope = sp.CreateScope();

    /// <summary>
    /// Creates the specified number of command bus instances within the scoped service provider.
    /// </summary>
    /// <param name="number">The number of command bus instances to create.</param>
    /// <returns>An enumerable of command bus instances.</returns>
    /// <remarks>
    /// <para>
    /// <b>CONSTRUCTS DIRECTLY; NEVER RESOLVES <c>ICommandBus</c>.</b> This override used to fill the pool
    /// with <c>_scope.ServiceProvider.GetRequiredService&lt;ICommandBus&gt;()</c> — the public, decorated
    /// service whose own factory calls <see cref="CommandBusPool.Init"/>, which calls this method. The pool
    /// built itself by resolving the very service it is the factory for, so the registration was
    /// self-referential.
    /// </para>
    /// <para>
    /// That cycle used to terminate by ACCIDENT: <c>Init()</c> assigned <c>_semaphore</c> before calling
    /// <c>Create()</c>, so the nested <c>Init()</c> saw a non-null flag and returned at depth 1. Nothing
    /// recorded that the ordering was load-bearing. When 1.2.5.0 fixed a genuine race by publishing
    /// <c>_semaphore</c> LAST, the accidental breaker went with it — and <c>lock</c> is re-entrant on the
    /// same thread, so nothing else stopped it. Every resolve then recursed until the host hung: in
    /// rocket-welder2, seven of fifteen routes stopped responding with nothing logged at any level.
    /// </para>
    /// <para>
    /// Constructing directly deletes the cycle instead of guarding it, and matches what the non-scoped
    /// <see cref="CommandBusPool.Create"/> has always done. The scoped path takes its <see cref="IPlumber"/>
    /// from this pool's OWN dedicated scope, which is the same instance the scoped <c>ICommandBus</c>
    /// factory would have handed it — the pooled buses are unchanged in everything but how they are built.
    /// They are also not decorated, which is correct: the decorators
    /// (<c>InProcCommandBusDecorator</c>, <c>CommandBusAttributeValidator</c>) belong to the caller-facing
    /// service, and a pooled bus is an internal transport rented by <see cref="CommandBusPool.RentScope"/>.
    /// </para>
    /// </remarks>
    public override IEnumerable<ICommandBus> Create(int number)
    {
        // Resolved ONCE, outside the loop, and from this pool's own scope: IPlumber is scoped, and every
        // bus in the pool must ride the same one.
        IPlumberApi pl = _scope.ServiceProvider.GetRequiredService<IPlumber>();
        var logger = _scope.ServiceProvider.GetRequiredService<ILogger<CommandBus>>();
        // `number`, not `_maxCount`: the override used to ignore its own parameter. The two agree today
        // because Init() passes _maxCount, so this fixes a latent disagreement rather than a live defect.
        for (int i = 0; i < number; ++i)
            yield return new CommandBus(pl, this, logger);
    }

    /// <inheritdoc/>
    /// <remarks>Disposes the pooled buses (via the base) and then the scope that supplied their plumber.</remarks>
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        switch (_scope)
        {
            case IAsyncDisposable a: await a.DisposeAsync(); break;
            default: _scope.Dispose(); break;
        }
    }
}
