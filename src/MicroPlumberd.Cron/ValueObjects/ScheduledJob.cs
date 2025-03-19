using EventStore.Client;

namespace MicroPlumberd.Services.Cron;

public readonly struct ScheduledJob : IComparable<ScheduledJob>, IEquatable<ScheduledJob>
{
    public readonly static ScheduledJob Empty = new ();
    public ScheduledJob()
    {
        this.JobDefinitionId = Guid.Empty;
        this.StartAt = DateTime.MinValue;
    }
    public static IComparer<ScheduledJob> TimeComparer { get; } = Comparer<ScheduledJob>.Create((x, y) => x.CompareTo(y,true));
    public ScheduledJob(Guid jobDefinitionId, DateTime startAt)
    {
        this.JobDefinitionId = jobDefinitionId;
        this.StartAt = startAt;
    }

    public Guid JobDefinitionId { get; }
    public DateTime StartAt { get; }



    public override int GetHashCode()
    {
        return JobDefinitionId.GetHashCode();
    }

    public void Deconstruct(out Guid jobDefinitionId, out DateTime startAt)
    {
        jobDefinitionId = this.JobDefinitionId;
        startAt = this.StartAt;
    }
    public int CompareTo(ScheduledJob other, bool timeFirst)
    {
        if(!timeFirst)
            return JobDefinitionId.CompareTo(other.JobDefinitionId);
        else
        {
            var tmp = StartAt.CompareTo(other.StartAt);
            return tmp == 0 ? this.JobDefinitionId.CompareTo(other.JobDefinitionId) : tmp;
        }
    }
    public int CompareTo(ScheduledJob other)
    {
        return JobDefinitionId.CompareTo(other.JobDefinitionId);
    }

    public bool Equals(ScheduledJob other)
    {
        return JobDefinitionId.Equals(other.JobDefinitionId);
    }

    public override bool Equals(object? obj)
    {
        return obj is ScheduledJob other && Equals(other);
    }

    public static bool operator ==(ScheduledJob left, ScheduledJob right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(ScheduledJob left, ScheduledJob right)
    {
        return !left.Equals(right);
    }
}