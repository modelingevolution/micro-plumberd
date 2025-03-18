namespace MicroPlumberd.Services.Cron;

public record CancelJobExecution
{
    public Guid Id { get; init; } = Guid.NewGuid();
}