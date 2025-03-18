namespace MicroPlumberd.Services.Cron;

public record StartJobExecution
{
    public Guid Id { get; init; } = Guid.NewGuid();
}