using System.Text.Json;
using System.Text.Json.Nodes;

namespace MicroPlumberd.Services.Cron;

public record JobExecutionStarted
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public JobId JobId { get; init; }
    public Guid CommandId { get; init; }
    public JsonElement Command { get; init; }
    public string CommandType { get; init; }
}