using System.Collections.Concurrent;
using System.Collections.ObjectModel;

namespace MicroPlumberd.Services.BatchOperations;

/// <summary>
/// In-memory read model for batch operations.
/// Handles batch operation events and maintains the current state of all operations.
/// </summary>
[OutputStream("BatchOperations")]
[EventHandler]
public partial class BatchOperationModel
{
    private readonly ConcurrentDictionary<Guid, BatchOperation> _operations = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _cancellationSources = new();
    private readonly ObservableCollection<BatchOperation> _items = new();

    /// <summary>
    /// Gets all active batch operations.
    /// </summary>
    public IReadOnlyList<BatchOperation> Items => _items;

    /// <summary>
    /// Gets all operations indexed by ID.
    /// </summary>
    public IReadOnlyDictionary<Guid, BatchOperation> Operations => _operations;

    /// <summary>
    /// Gets or creates a batch operation entry.
    /// </summary>
    public BatchOperation Get(Guid id) => _operations.GetOrAdd(id, x =>
    {
        BatchOperation tmp = new() { Id = x };
        _items.Add(tmp);
        return tmp;
    });

    /// <summary>
    /// Gets or creates a cancellation token source for an operation.
    /// </summary>
    public CancellationTokenSource GetCts(Guid id) => _cancellationSources.GetOrAdd(id, _ => new());

    /// <summary>
    /// Prepares a new operation and creates its cancellation source.
    /// </summary>
    /// <param name="operationId">The operation ID.</param>
    /// <returns>A cancellation token for the operation.</returns>
    public CancellationToken Prepare(Guid operationId) => GetCts(operationId).Token;

    /// <summary>
    /// Gets the cancellation token for an operation.
    /// </summary>
    /// <param name="operationId">The operation ID.</param>
    /// <returns>The cancellation token, or CancellationToken.None if not found.</returns>
    public CancellationToken GetCancellationToken(Guid operationId)
    {
        return _cancellationSources.TryGetValue(operationId, out var cts)
            ? cts.Token
            : CancellationToken.None;
    }

    /// <summary>
    /// Cancels an operation.
    /// </summary>
    /// <param name="operationId">The operation ID to cancel.</param>
    public void Cancel(Guid operationId)
    {
        if (_cancellationSources.TryGetValue(operationId, out var cts) && !cts.IsCancellationRequested)
        {
            cts.Cancel();
        }
    }

    /// <summary>
    /// Cleans up resources when an operation completes.
    /// </summary>
    /// <param name="operationId">The operation ID to clean up.</param>
    public void Cleanup(Guid operationId)
    {
        if (_cancellationSources.TryRemove(operationId, out var cts))
        {
            cts.Dispose();
        }

        if (_operations.TryRemove(operationId, out var o))
        {
            _items.Remove(o);
        }
    }

    // Event handlers
    private async Task Given(Metadata m, BatchOperationStarted evt)
    {
        var item = Get(evt.OperationId);
        item.Type = evt.OperationType;
        item.StartTime = m.Created() ?? DateTimeOffset.Now;
        item.Status = BatchOperation.State.Running;
        item.Progress = 0;
        item.AppContext = evt.AppContext;
    }

    private async Task Given(Metadata m, BatchOperationProgressed evt)
    {
        if (_operations.TryGetValue(evt.OperationId, out var vm))
        {
            vm.Progress = (float)evt.Current / evt.Total;
            vm.LastUpdateTime = DateTime.UtcNow;
            vm.CurrentMessage = evt.Message;
        }
    }

    private async Task Given(Metadata m, BatchOperationCompleted evt)
    {
        if (_operations.TryGetValue(evt.OperationId, out var vm))
        {
            vm.Status = evt.Success ? BatchOperation.State.Completed : BatchOperation.State.Failed;
            vm.EndTime = DateTime.UtcNow;
            vm.CurrentMessage = evt.Message;
            vm.Progress = evt.Success ? 1 : vm.Progress;
        }

        Cleanup(evt.OperationId);
    }

    private async Task Given(Metadata m, BatchOperationCancelled evt)
    {
        if (_operations.TryGetValue(evt.OperationId, out var vm))
        {
            vm.Status = BatchOperation.State.Canceled;
            vm.EndTime = DateTime.UtcNow;
            vm.CurrentMessage = evt.Reason;
        }

        Cleanup(evt.OperationId);
    }
}
