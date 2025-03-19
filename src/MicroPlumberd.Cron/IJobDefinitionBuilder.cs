namespace MicroPlumberd.Services.Cron;

public interface IJobDefinitionBuilder
{
    IJobDefinitionBuilder WithCommand<T>(T command, string recipient);
    IJobDefinitionBuilder WithSchedule(Schedule schedule);
    IJobDefinitionBuilder Enable();
    Task<JobDefinitionAggregate> Create();
}