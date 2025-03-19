namespace MicroPlumberd.Services.Cron;

public interface IJobItemsMonitor
{
    Task<JobItem<ScheduledJob>[]> ScheduledItems(CancellationToken token = default);
    Task<JobItem<RunningJob>[]> RunningItems(CancellationToken token = default);
}