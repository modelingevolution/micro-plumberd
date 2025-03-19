using System.Text.Json;
using System.Text.Json.Nodes;

namespace MicroPlumberd.Services.Cron;

[OutputStream("JobDefinition")]
[Aggregate]
public partial class JobDefinitionAggregate(Guid id) : AggregateBase<Guid, JobDefinitionAggregate.JobDefinitionState>(id)
{

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
    
    public void Rename(string name)
    {
        AppendPendingChange(new JobNamed(){Name=name});
    }
    public void Delete()
    {
        if (State.IsDeleted) return;

        if(State.Enabled)
            AppendPendingChange(new JobDisabled());
        AppendPendingChange(new JobDeleted());
    }
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
    public void Disable(string reason = null)
    {
        if(this.State.Enabled)
            AppendPendingChange(new JobDisabled(){Reason = reason});
    }
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