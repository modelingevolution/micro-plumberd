namespace MicroPlumberd.Services.Cron;

class JobService(IPlumber plumberd, JobDefinitionModel model) : IJobService
{
    public IJobDefinitionBuilder CreateBuilder(string name) => new JobDefinitionBuilder(plumberd, model, name);
    public async Task Enable(Guid jobDefinitionId)
    {
        var app = await plumberd.Get<JobDefinitionAggregate>(jobDefinitionId);
        app.Enable();
        await plumberd.SaveChanges(app);
    }

    public async Task Disable(Guid jobDefinitionId)
    {
        var app = await plumberd.Get<JobDefinitionAggregate>(jobDefinitionId);
        app.Disable();
        await plumberd.SaveChanges(app);
    }

    public async Task Delete(Guid jobDefinitionId)
    {
        var app = await plumberd.Get<JobDefinitionAggregate>(jobDefinitionId);
        app.Delete();
        await plumberd.SaveChanges(app);
    }

    public async Task RunOnce(Guid jobDefinitionId)
    {
        await plumberd.AppendEvent(new JobRunOnceEnqued() { JobDefinitionId = jobDefinitionId }, jobDefinitionId);
        
    }
}