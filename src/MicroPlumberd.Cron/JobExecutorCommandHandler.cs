using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace MicroPlumberd.Services.Cron;

/// <summary>
/// Handles job execution commands by managing the job lifecycle and dispatching commands to their recipients.
/// </summary>
[CommandHandler]
public partial class JobExecutorCommandHandler(IPlumber plumber, ICommandBus bus, ILogger<JobExecutorCommandHandler> log)
{
    /// <summary>
    /// Handles a job execution command by creating a job aggregate, executing the target command, and recording the result.
    /// </summary>
    /// <param name="id">The job identifier.</param>
    /// <param name="ev">The job execution command.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task Handle(JobId id, StartJobExecution ev)
    {
        var agg = await plumber.Get<JobAggregate>(id);
        
        await plumber.SaveNew(agg, streamMetadata: new StreamMetadata(){ MaxAge = TimeSpan.FromDays(7)});
        try
        {
            var obj = JsonSerializer.Deserialize(ev.Command, Type.GetType(ev.CommandType)) ?? throw new InvalidOperationException($"Cannot deserialize command {ev.CommandType}.");
            agg.Start(id, ev.Id, ev.CommandType, obj );

            log.LogDebug($"Executing command: {obj.GetType().Name} in job: {id}");
            await bus.QueueAsync(ev.Recipient, obj, fireAndForget: false, timeout: TimeSpan.FromHours(16));
            log.LogDebug($"Command executed: {obj.GetType().Name} in job: {id}");

            agg.Completed();
        }
        catch (Exception ex)
        {
            agg.Failed(ex.Message);
            
        }
        await plumber.SaveChanges(agg);
    }
        
}