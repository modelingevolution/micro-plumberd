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
    
    private ConcurrentStack<CommandBusOwner> _pool;
    private SemaphoreSlim _semaphore;
    private bool _disposed;
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

        if (_disposed)
        {
            cbo.CommandBus.DisposeAsync();
            return;
        }

        _pool.Push(cbo);
        _semaphore.Release();
    }
    /// <summary>
    /// Initializes the command bus pool by creating all command bus instances.
    /// </summary>
    /// <returns>This pool instance for method chaining.</returns>
    public ICommandBusPool Init()
    {
        if (_semaphore != null!) return this;
        _semaphore = new SemaphoreSlim(_maxCount);
        _pool = new ConcurrentStack<CommandBusOwner>(Create(number: _maxCount).Select(x=>new CommandBusOwner(this,x)));
        return this;
    }

    /// <inheritdoc/>
    public async ValueTask<ICommandBusOwner> RentScope(CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct);
        if (!_pool.TryPop(out var x))
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
    public virtual async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        
        if (_semaphore is IAsyncDisposable semaphoreAsyncDisposable)
            await semaphoreAsyncDisposable.DisposeAsync();
        else
            _semaphore.Dispose();

        foreach (var i in _pool.ToArray())
            await i.CommandBus.DisposeAsync();
    }
}