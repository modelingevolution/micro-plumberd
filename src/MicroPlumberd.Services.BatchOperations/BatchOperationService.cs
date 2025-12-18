namespace MicroPlumberd.Services.BatchOperations;

/// <summary>
/// Service for creating and managing batch operations.
/// </summary>
public class BatchOperationService
{
    private readonly IPlumberInstance _eventStore;
    private readonly BatchOperationModel _model;
    private readonly IAppContextProvider _appContext;

    /// <summary>
    /// Creates a new BatchOperationService.
    /// </summary>
    /// <param name="eventStore">The plumber instance for emitting events.</param>
    /// <param name="model">The batch operation model for state management.</param>
    /// <param name="appContext">The application context provider.</param>
    public BatchOperationService(IPlumberInstance eventStore, BatchOperationModel model, IAppContextProvider appContext)
    {
        _eventStore = eventStore;
        _model = model;
        _appContext = appContext;
    }

    /// <summary>
    /// Creates a new batch operation scope for tracking progress.
    /// </summary>
    /// <param name="operationType">The type/name of the operation.</param>
    /// <param name="start">The starting value of the range.</param>
    /// <param name="endInclusive">The ending value (inclusive) of the range.</param>
    /// <param name="opId">Optional operation ID. If not provided, a new GUID is generated.</param>
    /// <returns>A BatchOperationScope for tracking progress.</returns>
    public async Task<BatchOperationScope> CreateBatchScope(string operationType, double start, double endInclusive, Guid? opId = null)
    {
        // Prepare cancellation infrastructure
        var operationId = opId ?? Guid.NewGuid();
        var token = _model.Prepare(operationId);

        // Publish start event
        await _eventStore.AppendEvent(
            new BatchOperationStarted(operationId, operationType, start, endInclusive, _appContext.Context),
            operationId,
            token: token);

        // Return scope
        return new BatchOperationScope(operationId, _eventStore, _model);
    }

    /// <summary>
    /// Cleans up orphaned batch operations from previous application sessions.
    /// </summary>
    public async Task Cleanup()
    {
        var c = _appContext.Context;
        var h = c.AppInstance.Host;
        var app = c.AppInstance.Name;
        var node = c.AppInstance.Node;
        var toRemove = new List<Guid>();

        for (int i = 0; i < _model.Items.Count; i++)
        {
            try
            {
                var orphan = _model.Items[i];

                if (orphan.AppContext == AppContext.Empty ||
                    (orphan.AppContext.AppInstance.Host == h &&
                     orphan.AppContext.AppInstance.Name == app &&
                     orphan.AppContext.AppInstance.Node == node &&
                     orphan.AppContext.AppSession != c.AppSession))
                {
                    toRemove.Add(orphan.Id);
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                break;
            }
        }

        foreach (var i in toRemove)
            await _eventStore.AppendEvent(new BatchOperationCancelled(i, "Orphan detected"), i);
    }
}
