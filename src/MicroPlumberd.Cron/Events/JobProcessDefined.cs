using System.Text.Json;
using System.Text.Json.Nodes;

namespace MicroPlumberd.Services.Cron;

/// <summary>
/// Event indicating that a job's command process has been defined.
/// </summary>
[OutputStream("JobDefinition")]
public record JobProcessDefined
{
    /// <summary>
    /// Gets or sets the unique identifier for this event.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Gets or sets the assembly-qualified type name of the command.
    /// </summary>
    public string CommandType { get; init; }

    /// <summary>
    /// Gets or sets the command payload as a JSON element.
    /// </summary>
    public JsonElement CommandPayload { get; init; }

    /// <summary>
    /// Gets or sets the recipient identifier that will handle the command.
    /// </summary>
    public string Recipient { get; init; }
}