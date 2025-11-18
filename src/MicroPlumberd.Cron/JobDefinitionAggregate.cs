using System.Text.Json;
using System.Text.Json.Nodes;

namespace MicroPlumberd.Services.Cron;

/// <summary>
/// Represents the aggregate root for job definitions, managing the lifecycle and configuration of scheduled jobs.
/// </summary>
[OutputStream("JobDefinition")]
[Aggregate]
public partial class JobDefinitionAggregate(Guid id) : AggregateBase<Guid, JobDefinitionAggregate.JobDefinitionState>(id)
{
    /// <summary>
    /// Represents the state of a job definition aggregate.
    /// </summary>
    /// <param name="Enabled">Indicates whether the job is enabled.</param>
    /// <param name="ContainsSchedule">Indicates whether the job has a schedule defined.</param>
    /// <param name="ContainsCommand">Indicates whether the job has a command defined.</param>
    /// <param name="IsDeleted">Indicates whether the job has been deleted.</param>
    public readonly record struct JobDefinitionState(bool Enabled, bool ContainsSchedule, bool ContainsCommand, bool IsDeleted);

    private static JobDefinitionState Given(JobDefinitionState state, JobScheduleDefined ev)
    {
        return state with { ContainsSchedule = true };
    }
    private static JobDefinitionState Given(JobDefinitionState state, JobProcessDefined ev)
    {
        return state with { ContainsCommand = true };
    }
    private static JobDefinitionState Given(JobDefinitionState state, JobNamed ev)
    {
        return state;
    }

    private static JobDefinitionState Given(JobDefinitionState state, JobDeleted ev)
    {
        return state with { IsDeleted = true};
    }
    private static JobDefinitionState Given(JobDefinitionState state, JobEnabled ev) => state with { Enabled = true };
    private static JobDefinitionState Given(JobDefinitionState state, JobDisabled ev) => state with { Enabled = false };

    /// <summary>
    /// Renames the job definition.
    /// </summary>
    /// <param name="name">The new name for the job.</param>
    public void Rename(string name)
    {
        AppendPendingChange(new JobNamed(){Name=name});
    }

    /// <summary>
    /// Deletes the job definition, disabling it first if currently enabled.
    /// </summary>
    public void Delete()
    {
        if (State.IsDeleted) return;

        if(State.Enabled)
            AppendPendingChange(new JobDisabled());
        AppendPendingChange(new JobDeleted());
    }

    /// <summary>
    /// Defines the schedule for the job.
    /// </summary>
    /// <param name="s">The schedule configuration.</param>
    /// <exception cref="ArgumentNullException">Thrown when the schedule is null.</exception>
    public void DefineSchedule(Schedule s)
    {
        if (s == null) throw new ArgumentNullException("Schedule cannot be null");
        AppendPendingChange(new JobScheduleDefined() { Schedule = s});
    }
    private static JsonElement CopyWithoutProperty(in JsonElement original, string propertyToRemove)
    {
        // Enumerate the properties, filter out the one to remove, and create a dictionary
        var dict = original.EnumerateObject()
            .Where(p => p.Name != propertyToRemove)
            .ToDictionary(p => p.Name, p => p.Value);

        // Serialize the dictionary to a JSON string
        var newJson = JsonSerializer.Serialize(dict);

        // Parse the JSON string into a new JsonDocument
        using var newDoc = JsonDocument.Parse(newJson);

        // Return a clone of the RootElement to detach it from the document
        return newDoc.RootElement.Clone();
    }

    /// <summary>
    /// Defines the command and recipient for the job.
    /// </summary>
    /// <param name="command">The command instance to execute when the job runs.</param>
    /// <param name="recipient">The recipient identifier that will handle the command.</param>
    /// <exception cref="ArgumentNullException">Thrown when command or recipient is null.</exception>
    public void DefineCommand(object command, string recipient)
    {
        if(command == null || recipient == null)
            throw new ArgumentNullException("Command or recipient cannot be null.");

        var node = JsonSerializer.SerializeToElement(command);
        var dstNode = CopyWithoutProperty(node, "Id");

        AppendPendingChange(new JobProcessDefined()
        {
            CommandPayload = dstNode,
            CommandType = command.GetType().AssemblyQualifiedName!,
            Recipient = recipient
        });
    }

    /// <summary>
    /// Disables the job definition.
    /// </summary>
    /// <param name="reason">Optional reason for disabling the job.</param>
    public void Disable(string reason = null)
    {
        if(this.State.Enabled)
            AppendPendingChange(new JobDisabled(){Reason = reason});
    }

    /// <summary>
    /// Enables the job definition for execution.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when the job does not have both a schedule and command defined.</exception>
    /// <exception cref="InvalidOperationException">Thrown when attempting to enable a deleted job.</exception>
    public void Enable()
    {
        if (!this.State.ContainsSchedule || !this.State.ContainsCommand)
            throw new ArgumentException("Cannot enable job without schedule or process.");

        if(this.State.IsDeleted)
            throw new InvalidOperationException("Cannot enable a deleted job.");

        if (!this.State.Enabled)
            AppendPendingChange(new JobEnabled());
    }
}