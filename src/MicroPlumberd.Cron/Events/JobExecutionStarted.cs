using System.Text.Json;
using System.Text.Json.Nodes;

namespace MicroPlumberd.Services.Cron;

/// <summary>
/// Event indicating that a job execution has started.
/// </summary>
public record JobExecutionStarted
{
    /// <summary>
    /// Gets or sets the unique identifier for this event.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Gets or sets the job identifier.
    /// </summary>
    public JobId JobId { get; init; }

    /// <summary>
    /// Gets or sets the unique identifier of the command being executed.
    /// </summary>
    public Guid CommandId { get; init; }

    /// <summary>
    /// Gets or sets the command payload.
    /// </summary>
    public JsonElement Command { get; init; }

    /// <summary>
    /// Gets or sets the assembly-qualified type name of the command.
    /// </summary>
    public string CommandType { get; init; }
}

/// <summary>
/// Event indicating that a job has been manually enqueued for immediate execution.
/// </summary>
[OutputStream("JobManual")]
public record JobRunOnceEnqued
{
    /// <summary>
    /// Gets or sets the unique identifier for this event.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Gets or sets the job definition identifier to execute.
    /// </summary>
    public Guid JobDefinitionId { get; init; }
}