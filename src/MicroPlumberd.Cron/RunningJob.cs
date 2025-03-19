using System.Text.Json;

namespace MicroPlumberd.Services.Cron;

public readonly record struct RunningJob(Guid JobDefinitionId, JobId JobId, DateTimeOffset Created, string CommandType, JsonElement Command, ScheduleTrigger Trigger);