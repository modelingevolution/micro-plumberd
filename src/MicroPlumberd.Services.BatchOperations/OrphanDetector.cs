using Microsoft.Extensions.Hosting;

namespace MicroPlumberd.Services.BatchOperations;

/// <summary>
/// Background service that detects and cancels orphaned batch operations.
/// Orphans are operations started by previous app sessions that didn't complete.
/// </summary>
public class OrphanDetector : BackgroundService
{
    private readonly BatchOperationService _batchService;

    /// <summary>
    /// Creates a new OrphanDetector.
    /// </summary>
    /// <param name="batchService">The batch operation service.</param>
    public OrphanDetector(BatchOperationService batchService)
    {
        _batchService = batchService;
    }

    /// <inheritdoc />
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _ = Task.Run(async () =>
        {
            // Wait before checking for orphans to allow the system to stabilize
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            await _batchService.Cleanup();
        }, stoppingToken);

        return Task.CompletedTask;
    }
}
