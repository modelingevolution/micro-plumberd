namespace MicroPlumberd.Services.Cron;

public record JobItem<T>(JobDefinition Definition, T? Info);