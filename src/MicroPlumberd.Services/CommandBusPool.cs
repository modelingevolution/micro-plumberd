using System.Collections.Concurrent;
using MicroPlumberd.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MicroPlumberd.Services;

/// <summary>
/// Provides a pool of command bus instances that can be rented and returned for efficient resource usage.
/// </summary>
class CommandBusPool : IAsyncDisposable, ICommandBusPool
{
    /// <summary>
    /// Represents an owned command bus instance that can be returned to the pool.
    /// </summary>
    private class CommandBusOwner : ICommandBusOwner
    {
        private readonly CommandBusPool _parent;
        private readonly ICommandBus _commandBus;

        /// <inheritdoc/>
        public Task SendAsync(object recipientId, object command, TimeSpan? timeout = null, bool fireAndForget = false,
            CancellationToken token = default)
        {
            return _commandBus.SendAsync(recipientId, command, timeout, fireAndForget, token);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CommandBusOwner"/> class.
        /// </summary>
        /// <param name="parent">The parent pool that owns this instance.</param>
        /// <param name="cb">The command bus instance.</param>
        internal CommandBusOwner(CommandBusPool parent, ICommandBus cb)
        {
            _parent = parent;
            this._commandBus = cb;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _parent.Return(this);
        }

        /// <summary>
        /// Gets the underlying command bus instance.
        /// </summary>
        public ICommandBus CommandBus => _commandBus;
    }
    private readonly IServiceProvider _sp;
    protected readonly int _maxCount;
    
    private readonly object _initGate = new();
    private ConcurrentStack<CommandBusOwner>? _pool;
    private SemaphoreSlim? _semaphore;
    private int _disposed;
    /// <summary>
    /// Initializes a new instance of the <see cref="CommandBusPool"/> class.
    /// </summary>
    /// <param name="sp">The service provider for creating command bus instances.</param>
    /// <param name="maxCount">The maximum number of command bus instances in the pool.</param>
    public CommandBusPool(IServiceProvider sp, int maxCount)
    {
        _sp = sp;
        _maxCount = maxCount;
    }

    /// <summary>
    /// Returns a command bus owner to the pool.
    /// </summary>
    /// <param name="o">The command bus owner to return.</param>
    internal void Return(ICommandBusOwner o)
    {
        if (o is not CommandBusOwner cbo)
            throw new ArgumentException();

        if (Volatile.Read(ref _disposed) != 0)
        {
            cbo.CommandBus.DisposeAsync();
            return;
        }

        // The pool can only be non-null here (a rent implies an Init), but read both fields once into
        // locals: DisposeAsync may be clearing them concurrently on another thread.
        var pool = _pool;
        var semaphore = _semaphore;
        if (pool is null || semaphore is null)
        {
            cbo.CommandBus.DisposeAsync();
            return;
        }

        pool.Push(cbo);
        semaphore.Release();
    }
    /// <summary>
    /// Initializes the command bus pool by creating all command bus instances.
    /// </summary>
    /// <returns>This pool instance for method chaining.</returns>
    /// <remarks>
    /// Idempotent and thread-safe. On the SCOPED registration path this is called lazily, from the
    /// <c>ICommandBus</c> factory, which several request threads can enter at once. The previous shape
    /// published <c>_semaphore</c> BEFORE <c>_pool</c> was built, so a second caller saw a non-null
    /// semaphore, returned early, and then hit a null <c>_pool</c> inside <see cref="RentScope"/>.
    /// Building under a gate and publishing <c>_pool</c> first closes that window.
    /// </remarks>
    public ICommandBusPool Init()
    {
        if (_semaphore is not null) return this;
        lock (_initGate)
        {
            if (_semaphore is not null) return this;
            _pool = new ConcurrentStack<CommandBusOwner>(Create(number: _maxCount).Select(x=>new CommandBusOwner(this,x)));
            _semaphore = new SemaphoreSlim(_maxCount);   // published LAST: it is the initialised flag
        }
        return this;
    }

    /// <inheritdoc/>
    public async ValueTask<ICommandBusOwner> RentScope(CancellationToken ct = default)
    {
        // Deliberately does NOT call Init(): on the scoped path Create() resolves ICommandBus, whose
        // factory calls Init() itself, so self-initialising here would re-enter the (re-entrant) init
        // lock on the same thread and recurse until the stack blows. Fail loudly instead of NRE-ing.
        var semaphore = _semaphore ?? throw new InvalidOperationException(
            $"{nameof(CommandBusPool)} has not been initialised (or is already disposed). Call Init() first.");
        var pool = _pool ?? throw new InvalidOperationException(
            $"{nameof(CommandBusPool)} has not been initialised (or is already disposed). Call Init() first.");
        await semaphore.WaitAsync(ct);
        if (!pool.TryPop(out var x))
            throw new InvalidOperationException();
        return x;
    }
    /// <summary>
    /// Creates the specified number of command bus instances.
    /// </summary>
    /// <param name="number">The number of command bus instances to create.</param>
    /// <returns>An enumerable of command bus instances.</returns>
    public virtual IEnumerable<ICommandBus> Create(int number)
    {
        // Command is configured to be singleton in the container.
        IPlumberApi pl = _sp.GetRequiredService<IPlumberInstance>();
        var logger = _sp.GetRequiredService<ILogger<CommandBus>>();
        for (int i = 0; i < number; ++i) 
            yield return new CommandBus(pl,this, logger);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// IDEMPOTENT, AND SAFE ON A POOL THAT WAS NEVER INITIALISED. On the SCOPED registration path
    /// <c>Init()</c> is deferred to the first resolve of a scoped <c>ICommandBus</c>, so a host that is
    /// built and torn down without ever sending a command disposes a pool whose <c>_semaphore</c> and
    /// <c>_pool</c> are still null. The previous shape dereferenced both unconditionally and threw
    /// <see cref="NullReferenceException"/> out of the DI container's disposal, failing the whole
    /// host teardown. An uninitialised pool is a legal state — there is simply nothing to release.
    /// </remarks>
    public virtual async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        // Claim both handles so a concurrent Return()/RentScope sees null and bails out instead of
        // touching a semaphore this method is disposing.
        var semaphore = Interlocked.Exchange(ref _semaphore, null);
        var pool = Interlocked.Exchange(ref _pool, null);

        if (semaphore is IAsyncDisposable semaphoreAsyncDisposable)
            await semaphoreAsyncDisposable.DisposeAsync();
        else
            semaphore?.Dispose();

        foreach (var i in pool?.ToArray() ?? [])
            await i.CommandBus.DisposeAsync();
    }
}