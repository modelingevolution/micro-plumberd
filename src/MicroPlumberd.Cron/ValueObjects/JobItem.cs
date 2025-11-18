namespace MicroPlumberd.Services.Cron;

/// <summary>
/// Represents a pairing of a job definition with additional type-specific information.
/// </summary>
/// <typeparam name="T">The type of additional information (e.g., ScheduledJob or RunningJob).</typeparam>
/// <param name="Definition">The job definition.</param>
/// <param name="Info">The additional type-specific information about the job.</param>
public record JobItem<T>(JobDefinition Definition, T? Info);