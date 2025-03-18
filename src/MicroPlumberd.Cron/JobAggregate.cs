using System.Text.Json;
using System.Text.Json.Nodes;

namespace MicroPlumberd.Services.Cron;

[OutputStream("Job")]
[Aggregate]
public partial class JobAggregate(JobId id) : AggregateBase<JobId, JobAggregate.JobState>(id)
{
    public void Start(JobId id, Guid commandId, string commandType, object commandPayload)
    {
        if (State.Started) throw new InvalidOperationException("Job already started");
        if (commandId == Guid.Empty)
            throw new ArgumentException("CommandId");

        this.AppendPendingChange(new JobExecutionStarted()
        {
            JobId = id, 
            CommandId = commandId, 
            Command = JsonSerializer.SerializeToElement(commandPayload),
            CommandType = commandType
        });
        
    }
    public readonly record struct JobState(bool Started, bool Finished)
    {
        
    }

    private static JobState Given(JobState state, JobExecutionStarted ev) => state with { Started = true };
    private static JobState Given(JobState state, JobExecutionCompleted ev) => state with { Finished = true };
    
    private static JobState Given(JobState state, JobExecutionFailed ev) => state with { Finished = true };
}