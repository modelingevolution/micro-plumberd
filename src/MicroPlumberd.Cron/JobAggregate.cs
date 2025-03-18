using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MicroPlumberd.Cron;

public readonly record struct JobId(Guid JobDefinitionId, DateTime At) : IParsable<JobId>
{
    public override string ToString()
    {
        return $"{At.ToBinary()}/{JobDefinitionId}";
    }

    public static JobId Parse(string s, IFormatProvider? provider)
    {
        var seg= s.Split('/');
        return new JobId(Guid.Parse(seg[1]), DateTime.FromBinary(long.Parse(seg[0])));
    }

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out JobId result)
    {
        if (s != null)
        {
            var seg = s.Split('/');
            if (seg.Length == 2)
            {
                if (Guid.TryParse(seg[1], out var guid) && long.TryParse(seg[0], out var dt))
                {
                    result = new JobId(guid, DateTime.FromBinary(dt));
                    return true;
                }
            }
        }
        result = default;
        return false;
    }
}
[OutputStream("Job")]
[Aggregate]
public partial class JobAggregate(JobId id) : AggregateBase<JobId, JobAggregate.JobState>(id)
{
    public void Start(JobId id, Guid commandId, string commandType, JsonObject commandPayload)
    {
        if (State.Started) throw new InvalidOperationException("Job already started");
        if (commandId == Guid.Empty)
            throw new ArgumentException("CommandId");

        this.AppendPendingChange(new JobExecutionStarted()
        {
            JobId = id, 
            CommandId = commandId, 
            Command = commandPayload,
            CommandType = commandType
        });
        
    }
    public readonly record struct JobState(bool Started, bool Finished)
    {
        
    }

    private static JobState Given(JobState state, JobExecutionStarted ev) => state with { Started = true };
    private static JobState Given(JobState state, JobExecutionCompleted ev) => state with { Finished = true };
    
    private static JobState Given(JobState state, JobExecutionFailed ev) => state with { Finished = true };
}