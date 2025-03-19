namespace MicroPlumberd.Services.Cron;

[OutputStream("JobDefinition")]
public record JobNamed
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; }
}


[OutputStream("JobDefinition")]
public record JobDeleted
{
    public Guid Id { get; init; } = Guid.NewGuid();
}