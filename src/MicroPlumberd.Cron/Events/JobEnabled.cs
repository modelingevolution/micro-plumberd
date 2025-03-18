namespace MicroPlumberd.Services.Cron;

[OutputStream("JobDefinition")]
public record JobEnabled
{
    public Guid Id { get; init; } = Guid.NewGuid();
}