namespace MicroPlumberd.Services.Cron;

[OutputStream("JobDefinition")]
public record JobNamed
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; }
}