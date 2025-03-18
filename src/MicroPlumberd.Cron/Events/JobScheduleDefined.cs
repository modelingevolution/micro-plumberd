namespace MicroPlumberd.Services.Cron;

[OutputStream("JobDefinition")]
public record JobScheduleDefined
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Schedule Schedule { get; init; }
}