using System.Collections.Concurrent;
using MicroPlumberd.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MicroPlumberd.Services;

class CommandBusPool : IAsyncDisposable, ICommandBusPool
{
    private class CommandBusOwner : ICommandBusOwner
    {
        private readonly CommandBusPool _parent;
        private readonly ICommandBus _commandBus;

        public Task SendAsync(object recipientId, object command, TimeSpan? timeout = null, bool fireAndForget = false,
            CancellationToken token = default)
        {
            return _commandBus.SendAsync(recipientId, command, timeout, fireAndForget, token);
        }


        internal CommandBusOwner(CommandBusPool parent, ICommandBus cb)
        {
            _parent = parent;
            this._commandBus = cb;
        }

        public void Dispose()
        {
            _parent.Return(this);
        }

        public ICommandBus CommandBus => _commandBus;
    }
    private readonly IServiceProvider _sp;
    protected readonly int _maxCount;
    
    private ConcurrentStack<CommandBusOwner> _pool;
    private SemaphoreSlim _semaphore;
    private bool _disposed;
    public CommandBusPool(IServiceProvider sp, int maxCount)
    {
        _sp = sp;
        _maxCount = maxCount;
    }

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
    public ICommandBusPool Init()
    {
        if (_semaphore != null!) return this;
        _semaphore = new SemaphoreSlim(_maxCount);
        _pool = new ConcurrentStack<CommandBusOwner>(Create(number: _maxCount).Select(x=>new CommandBusOwner(this,x)));
        return this;
    }

    public async ValueTask<ICommandBusOwner> RentScope(CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct);
        if (!_pool.TryPop(out var x))
            throw new InvalidOperationException();
        return x;
    }
    public virtual IEnumerable<ICommandBus> Create(int number)
    {
        // Command is configured to be singleton in the container.
        IPlumberApi pl = _sp.GetRequiredService<IPlumberInstance>();
        var logger = _sp.GetRequiredService<ILogger<CommandBus>>();
        for (int i = 0; i < number; ++i) 
            yield return new CommandBus(pl,this, logger);
    }

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