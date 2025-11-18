using System.Text.Json;
using System.Text.Json.Nodes;

namespace MicroPlumberd.Services.Cron;

/// <summary>
/// Represents the aggregate root for individual job executions, tracking the lifecycle of a single job run.
/// </summary>
[OutputStream("Job")]
[Aggregate]
public partial class JobAggregate(JobId id) : AggregateBase<JobId, JobAggregate.JobState>(id)
{
    /// <summary>
    /// Starts the job execution.
    /// </summary>
    /// <param name="id">The job identifier.</param>
    /// <param name="commandId">The unique identifier of the command being executed.</param>
    /// <param name="commandType">The type name of the command being executed.</param>
    /// <param name="commandPayload">The command payload to execute.</param>
    /// <exception cref="InvalidOperationException">Thrown when the job has already started.</exception>
    /// <exception cref="ArgumentException">Thrown when the command ID is empty.</exception>
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

    /// <summary>
    /// Marks the job execution as completed successfully.
    /// </summary>
    public void Completed()
    {
        AppendPendingChange(new JobExecutionCompleted());
    }

    /// <summary>
    /// Marks the job execution as failed with an error message.
    /// </summary>
    /// <param name="error">The error message describing why the job failed.</param>
    public void Failed(string error)
    {
        AppendPendingChange(new JobExecutionFailed() { Error = error});
    }

    /// <summary>
    /// Represents the state of a job execution.
    /// </summary>
    /// <param name="Started">Indicates whether the job has started.</param>
    /// <param name="Finished">Indicates whether the job has finished (either completed or failed).</param>
    public readonly record struct JobState(bool Started, bool Finished)
    {

    }

    private static JobState Given(JobState state, JobExecutionStarted ev) => state with { Started = true };
    private static JobState Given(JobState state, JobExecutionCompleted ev) => state with { Finished = true };

    private static JobState Given(JobState state, JobExecutionFailed ev) => state with { Finished = true };
}