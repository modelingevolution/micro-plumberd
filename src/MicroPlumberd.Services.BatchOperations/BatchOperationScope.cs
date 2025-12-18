using System.Diagnostics;

namespace MicroPlumberd.Services.BatchOperations;

/// <summary>
/// Provides a scope for tracking the progress of a batch operation.
/// Implements both IDisposable and IAsyncDisposable for proper cleanup.
/// </summary>
public class BatchOperationScope : IDisposable, IAsyncDisposable
{
    private readonly Guid _operationId;
    private readonly IPlumberInstance _eventStore;
    private readonly BatchOperationModel _model;
    private readonly TimeSpan _every;
    private bool _isCompleted;
    private readonly Stopwatch _lastReport = Stopwatch.StartNew();

    /// <summary>
    /// Creates a new batch operation scope.
    /// </summary>
    /// <param name="operationId">The unique identifier of the operation.</param>
    /// <param name="eventStore">The plumber instance for emitting events.</param>
    /// <param name="model">The batch operation model for state management.</param>
    /// <param name="every">The minimum interval between progress reports. Defaults to 1 second.</param>
    public BatchOperationScope(Guid operationId, IPlumberInstance eventStore, BatchOperationModel model, TimeSpan? every = null)
    {
        _operationId = operationId;
        _eventStore = eventStore;
        _model = model;
        _every = every ?? TimeSpan.FromSeconds(1);
    }

    /// <summary>
    /// Gets the operation ID.
    /// </summary>
    public Guid OperationId => _operationId;

    /// <summary>
    /// Gets the cancellation token for this operation.
    /// </summary>
    public CancellationToken CancellationToken => _model.GetCancellationToken(_operationId);

    /// <summary>
    /// Executes a parallel foreach operation with progress tracking.
    /// </summary>
    /// <typeparam name="T">The type of items to process.</typeparam>
    /// <param name="items">The items to process.</param>
    /// <param name="onAction">The action to execute for each item. Receives index, item, and cancellation token.</param>
    /// <param name="maxDegreeOfParallelism">Maximum parallelism. 0 uses processor count.</param>
    /// <param name="msg">Optional function to generate status messages.</param>
    /// <param name="token">Additional cancellation token.</param>
    public async Task ParallelForeach<T>(IReadOnlyList<T> items,
        Func<int, T, CancellationToken, ValueTask> onAction, int maxDegreeOfParallelism = 0,
        Func<T, int, string>? msg = null, CancellationToken token = default)
    {
        ParallelOptions options = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxDegreeOfParallelism == 0 ? Environment.ProcessorCount : maxDegreeOfParallelism,
            CancellationToken = token
        };
        int counter = 0;
        await Parallel.ForAsync(0, items.Count, options, async (i, ct) =>
        {
            await onAction(i, items[i], ct);
            await Progress(Interlocked.Increment(ref counter), items.Count, msg?.Invoke(items[i], i));
        });
    }

    /// <summary>
    /// Executes a parallel foreach operation with progress tracking (index-based).
    /// </summary>
    /// <typeparam name="T">The type of items to process.</typeparam>
    /// <param name="items">The items to process.</param>
    /// <param name="onAction">The action to execute for each index.</param>
    /// <param name="options">Parallel options.</param>
    /// <param name="msg">Optional function to generate status messages.</param>
    public async Task ParallelForeach<T>(IReadOnlyList<T> items,
        Func<int, CancellationToken, ValueTask> onAction, ParallelOptions? options = null,
        Func<T, int, string>? msg = null)
    {
        options ??= new ParallelOptions();
        int counter = 0;
        await Parallel.ForAsync(0, items.Count, options, async (i, token) =>
        {
            await onAction(i, token);
            await Progress(Interlocked.Increment(ref counter), items.Count, msg?.Invoke(items[i], i));
        });
    }

    /// <summary>
    /// Iterates through items with progress tracking, yielding index and item pairs.
    /// </summary>
    /// <typeparam name="T">The type of items.</typeparam>
    /// <param name="items">The items to iterate.</param>
    /// <param name="msg">Optional function to generate status messages.</param>
    /// <returns>An async enumerable of index and item pairs.</returns>
    public async IAsyncEnumerable<(int Index, T Item)> For<T>(IReadOnlyList<T> items, Func<T, int, string>? msg = null)
    {
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            yield return (i, item);
            await Progress(i + 1, items.Count, msg?.Invoke(item, i));
        }
    }

    /// <summary>
    /// Iterates through items with progress tracking.
    /// </summary>
    /// <typeparam name="T">The type of items.</typeparam>
    /// <param name="items">The items to iterate.</param>
    /// <param name="msg">Optional function to generate status messages.</param>
    /// <returns>An async enumerable of items.</returns>
    public async IAsyncEnumerable<T> Foreach<T>(IReadOnlyList<T> items, Func<T, int, string>? msg = null)
    {
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            yield return item;
            await Progress(i + 1, items.Count, msg?.Invoke(item, i));
        }
    }

    /// <summary>
    /// Reports progress with long values.
    /// </summary>
    /// <param name="current">Current progress count.</param>
    /// <param name="total">Total item count.</param>
    /// <param name="message">Optional status message.</param>
    public Task Progress(long current, long total, string? message = null)
    {
        if (current >= 0L && total >= 0L)
            return Progress((ulong)current, (ulong)total, message);
        else throw new ArgumentOutOfRangeException(nameof(current), "Parameters must be positive");
    }

    private bool ShouldTrigger()
    {
        lock (this)
        {
            if (_lastReport.Elapsed <= _every) return false;
            _lastReport.Restart();
            return true;
        }
    }

    /// <summary>
    /// Reports progress with ulong values.
    /// </summary>
    /// <param name="current">Current progress count.</param>
    /// <param name="total">Total item count.</param>
    /// <param name="message">Optional status message.</param>
    public async Task Progress(ulong current, ulong total, string? message = null)
    {
        if (CancellationToken.IsCancellationRequested)
            throw new OperationCanceledException("Operation canceled");

        if (ShouldTrigger())
        {
            await _eventStore.AppendEvent(new BatchOperationProgressed(_operationId, current, total, message),
                OperationId, token: CancellationToken);
        }
    }

    /// <summary>
    /// Completes the operation successfully.
    /// </summary>
    /// <param name="message">Optional completion message.</param>
    public async Task Complete(string message = "Operation completed successfully")
    {
        if (!_isCompleted)
        {
            await _eventStore.AppendEvent(new BatchOperationCompleted(_operationId, true, message), _operationId);
            _isCompleted = true;
        }
    }

    /// <summary>
    /// Fails the operation with an error message.
    /// </summary>
    /// <param name="errorMessage">The error message.</param>
    public async Task Fail(string errorMessage)
    {
        if (!_isCompleted)
        {
            await _eventStore.AppendEvent(new BatchOperationCompleted(_operationId, false, errorMessage), _operationId);
            _isCompleted = true;
        }
    }

    /// <summary>
    /// Cancels the operation.
    /// </summary>
    /// <param name="reason">The cancellation reason.</param>
    public async Task Cancel(string reason = "Operation cancelled by user")
    {
        if (!_isCompleted)
        {
            await _eventStore.AppendEvent(new BatchOperationCancelled(_operationId, reason), _operationId);
            _isCompleted = true;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_isCompleted)
        {
            // Fire and forget failure notification
            _ = Fail("Operation was disposed without proper completion");
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (!_isCompleted)
        {
            await Fail("Operation was disposed without proper completion");
        }
    }
}
