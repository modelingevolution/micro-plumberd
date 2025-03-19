using Microsoft.Extensions.Hosting;

namespace MicroPlumberd.Services.Cron;

public class JobExecutionBackgroundService(JobExecutionProcessor processor, IJobsMonitor monitor /*DONT REMOVE IT!*/) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await processor.StartAsync(stoppingToken);
    }
}