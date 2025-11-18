using Microsoft.Extensions.Hosting;

namespace MicroPlumberd.Services.Cron;

/// <summary>
/// Background service that runs the job execution processor.
/// </summary>
/// <remarks>
/// The monitor parameter ensures proper initialization order and should not be removed.
/// </remarks>
public class JobExecutionBackgroundService(JobExecutionProcessor processor, IJobsMonitor monitor /*DONT REMOVE IT!*/) : BackgroundService
{
    /// <summary>
    /// Executes the background service by starting the job execution processor.
    /// </summary>
    /// <param name="stoppingToken">A cancellation token to stop the service.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await processor.StartAsync(stoppingToken);
    }
}