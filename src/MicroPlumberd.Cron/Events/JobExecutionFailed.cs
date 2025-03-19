namespace MicroPlumberd.Services.Cron;

public record JobExecutionFailed
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Error { get; init; }
}