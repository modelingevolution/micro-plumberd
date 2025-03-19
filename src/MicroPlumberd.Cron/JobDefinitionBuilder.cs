namespace MicroPlumberd.Services.Cron;

class JobDefinitionBuilder(IPlumber plumberd, JobDefinitionModel model, string name) : IJobDefinitionBuilder
{
    private object? _command;
    private string _recipient;
    private Schedule? _schedule;
    private bool _isEnabled;
    public IJobDefinitionBuilder WithCommand<T>(T command, string recipient)
    {
        _command = command;
        _recipient = recipient;
        return this;
    }
    public IJobDefinitionBuilder WithSchedule(Schedule schedule)
    {
        _schedule = schedule;
        return this;
    }

    public IJobDefinitionBuilder Enable()
    {
        _isEnabled = true;
        return this;
    }

    public async Task RunOnce(Guid jobDefinitionId)
    {

    }
    public async Task<JobDefinitionAggregate> Create()
    {
        JobDefinitionAggregate agg = JobDefinitionAggregate.Empty(Guid.NewGuid());
        if (model.JobDefinitions.Any(x => x.Name == name && !x.IsDeleted))
            throw new InvalidOperationException("Job with the same name already exists.");
        agg.Rename(name);
        agg.DefineCommand(_command, _recipient);
        agg.DefineSchedule(_schedule);
        if (_isEnabled)
            agg.Enable();
        await plumberd.SaveNew(agg);
        return agg;
    }
}