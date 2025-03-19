namespace MicroPlumberd.Services.Cron;

public interface IJobService
{
    IJobDefinitionBuilder CreateBuilder(string name);
    Task RunOnce(Guid jobDefinitionId);
    Task Enable(Guid jobDefinitionId);
    Task Disable(Guid jobDefinitionId);
    Task Delete(Guid jobDefinitionId);
}