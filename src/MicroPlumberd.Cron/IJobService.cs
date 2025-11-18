namespace MicroPlumberd.Services.Cron;

/// <summary>
/// Provides methods for managing job definitions and their execution lifecycle.
/// </summary>
public interface IJobService
{
    /// <summary>
    /// Creates a new builder for defining a job with the specified name.
    /// </summary>
    /// <param name="name">The name of the job to create.</param>
    /// <returns>A job definition builder for configuring the job.</returns>
    IJobDefinitionBuilder CreateBuilder(string name);

    /// <summary>
    /// Executes a job once immediately, bypassing its normal schedule.
    /// </summary>
    /// <param name="jobDefinitionId">The unique identifier of the job definition to execute.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task RunOnce(Guid jobDefinitionId);

    /// <summary>
    /// Enables a job definition, allowing it to be scheduled for execution.
    /// </summary>
    /// <param name="jobDefinitionId">The unique identifier of the job definition to enable.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task Enable(Guid jobDefinitionId);

    /// <summary>
    /// Disables a job definition, preventing it from being scheduled for execution.
    /// </summary>
    /// <param name="jobDefinitionId">The unique identifier of the job definition to disable.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task Disable(Guid jobDefinitionId);

    /// <summary>
    /// Deletes a job definition permanently.
    /// </summary>
    /// <param name="jobDefinitionId">The unique identifier of the job definition to delete.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task Delete(Guid jobDefinitionId);
}