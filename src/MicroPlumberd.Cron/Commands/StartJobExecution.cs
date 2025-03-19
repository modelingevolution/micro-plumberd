using System.Text.Json;

namespace MicroPlumberd.Services.Cron;

public record StartJobExecution
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public JsonElement Command { get; init; }
    public string CommandType { get; init; }
    public string Recipient { get; init; }
}