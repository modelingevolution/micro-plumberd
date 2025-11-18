namespace MicroPlumberd.Services.Cron;

/// <summary>
/// Specifies how a job execution was triggered.
/// </summary>
public enum ScheduleTrigger
{
    /// <summary>
    /// The job was triggered automatically by the scheduling engine.
    /// </summary>
    Engine,

    /// <summary>
    /// The job was triggered manually by a user or system action.
    /// </summary>
    Manual
}