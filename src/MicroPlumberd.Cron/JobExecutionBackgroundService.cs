using Microsoft.Extensions.Hosting;

namespace MicroPlumberd.Services.Cron;

public class JobExecutionBackgroundService(JobExecutionProcessor processor) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await processor.StartAsync(stoppingToken);
    }
}