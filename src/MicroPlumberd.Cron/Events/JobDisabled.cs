namespace MicroPlumberd.Services.Cron;

[OutputStream("JobDefinition")]
public record JobDisabled
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Reason { get; init; }
}