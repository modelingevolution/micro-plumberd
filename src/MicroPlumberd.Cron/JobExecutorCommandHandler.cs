using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace MicroPlumberd.Services.Cron;

[CommandHandler]
public partial class JobExecutorCommandHandler(IPlumber plumber, ICommandBus bus, ILogger<JobExecutorCommandHandler> log)
{
    public async Task Handle(JobId id, StartJobExecution ev)
    {
        var agg = await plumber.Get<JobAggregate>(id);
        
        await plumber.SaveNew(agg);
        try
        {
            var obj = JsonSerializer.Deserialize(ev.Command, Type.GetType(ev.CommandType)) ?? throw new InvalidOperationException($"Cannot deserialize command {ev.CommandType}.");
            agg.Start(id, ev.Id, ev.CommandType, obj );

            log.LogInformation($"Executing command: {obj.GetType().Name} in job: {id}");
            await bus.QueueAsync(ev.Recipient, obj, fireAndForget: false, timeout: TimeSpan.FromHours(16));
            log.LogInformation($"Command executed: {obj.GetType().Name} in job: {id}");

            agg.Completed();
        }
        catch (Exception ex)
        {
            agg.Failed(ex.Message);
            
        }
        await plumber.SaveChanges(agg);
    }
        
}