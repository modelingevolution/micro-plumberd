namespace MicroPlumberd.Services.Cron;

public record JobExecutionCompleted
{
    public Guid Id { get; init; } = Guid.NewGuid();
}