using System.Text.Json;
using System.Text.Json.Nodes;

namespace MicroPlumberd.Services.Cron;

[OutputStream("JobDefinition")]
public record JobProcessDefined
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string CommandType { get; init; }
    public JsonElement CommandPayload { get; init; }
    public string Recipient { get; init; }
}