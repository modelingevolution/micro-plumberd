using System.Text.Json;

namespace MicroPlumberd.Services.Cron;

/// <summary>
/// Command to start a job execution.
/// </summary>
public record StartJobExecution
{
    /// <summary>
    /// Gets or sets the unique identifier for this command.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Gets or sets the command payload to execute.
    /// </summary>
    public JsonElement Command { get; init; }

    /// <summary>
    /// Gets or sets the assembly-qualified type name of the command.
    /// </summary>
    public string CommandType { get; init; }

    /// <summary>
    /// Gets or sets the recipient identifier that will handle the command.
    /// </summary>
    public string Recipient { get; init; }
}