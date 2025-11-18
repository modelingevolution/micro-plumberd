using System.Text.Json;

namespace MicroPlumberd.Services.Cron;

/// <summary>
/// Represents a job that is currently executing.
/// </summary>
/// <param name="JobDefinitionId">The unique identifier of the job definition.</param>
/// <param name="JobId">The unique identifier of this job execution instance.</param>
/// <param name="Created">The time when the job execution was created.</param>
/// <param name="CommandType">The assembly-qualified type name of the command being executed.</param>
/// <param name="Command">The command payload as a JSON element.</param>
/// <param name="Trigger">The trigger that initiated this job execution.</param>
public readonly record struct RunningJob(Guid JobDefinitionId, JobId JobId, DateTimeOffset Created, string CommandType, JsonElement Command, ScheduleTrigger Trigger);