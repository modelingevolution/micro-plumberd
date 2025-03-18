namespace MicroPlumberd.Cron;

[OutputStream("JobDefinition")]
[Aggregate]
public partial class JobDefinitionAggregate(Guid id) : AggregateBase<Guid, JobDefinitionAggregate.JobDefinitionState>(id)
{

    public readonly record struct JobDefinitionState(bool Enabled);

    private static JobDefinitionState Given(JobDefinitionState state, JobScheduleDefined ev)
    {
        return state;
    }
    private static JobDefinitionState Given(JobDefinitionState state, JobProcessDefined ev)
    {
        return state;
    }
    private static JobDefinitionState Given(JobDefinitionState state, JobNamed ev)
    {
        return state;
    }

    private static JobDefinitionState Given(JobDefinitionState state, JobEnabled ev) => state with { Enabled = true };
    private static JobDefinitionState Given(JobDefinitionState state, JobDisabled ev) => state with { Enabled = false };
    
    public void Rename(string name)
    {
        AppendPendingChange(new JobNamed(){Name=name});
    }
    public void Disable(string reason = null)
    {
        if(this.State.Enabled)
            AppendPendingChange(new JobDisabled(){Reason = reason});
    }
}
[OutputStream("JobDefinition")]
public record JobScheduleDefined
{
    public Guid Id { get; init; } = Guid.NewGuid();
}


[OutputStream("JobDefinition")]
public record JobProcessDefined
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string CommandType { get; init; }
    public string CommandPayload { get; init; }
    public string Recipient { get; init; }
}



[OutputStream("JobDefinition")]
public record JobNamed
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; }
}
[OutputStream("JobDefinition")]
public record JobEnabled
{
    public Guid Id { get; init; } = Guid.NewGuid();
}
[OutputStream("JobDefinition")]
public record JobDisabled
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Reason { get; init; }
}
