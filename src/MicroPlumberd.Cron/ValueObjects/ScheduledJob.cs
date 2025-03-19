using System.Collections;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;
using EventStore.Client;

namespace MicroPlumberd.Services.Cron;

public static class Vector128Comparer
{
    public static readonly Comparison<Vector128<byte>> Compare;

    static Vector128Comparer()
    {
        Compare = Sse2.IsSupported ? CompareSse2 : (AdvSimd.IsSupported ? CompareArm : CompareDumb);
    }

    public static int CompareSse2(Vector128<byte> a, Vector128<byte> b)
    {

        Vector128<byte> min = Sse2.Min(a, b);
        Vector128<byte> max = Sse2.Max(a, b);

        if (Sse2.MoveMask(Sse2.CompareEqual(a, b)) == 0xFFFF)
            return 0; // a == b

        return Sse2.MoveMask(Sse2.CompareEqual(a, min)) == 0xFFFF ? -1 : 1;
    }

    public static int CompareArm(Vector128<byte> a, Vector128<byte> b)
    {

        Vector128<byte> min = AdvSimd.Min(a, b);
        Vector128<byte> max = AdvSimd.Max(a, b);

        if (AdvSimd.CompareEqual(a, b).AsUInt64().GetElement(0) == ulong.MaxValue &&
            AdvSimd.CompareEqual(a, b).AsUInt64().GetElement(1) == ulong.MaxValue)
            return 0; // a == b

        return AdvSimd.CompareEqual(a, min).AsUInt64().GetElement(0) == ulong.MaxValue &&
               AdvSimd.CompareEqual(a, min).AsUInt64().GetElement(1) == ulong.MaxValue
            ? -1
            : 1;
    }

    public static int CompareDumb(Vector128<byte> a, Vector128<byte> b)
    {


        // Fallback (non-SIMD) comparison
        for (int i = 0; i < Vector128<byte>.Count; i++)
        {
            byte ai = a.GetElement(i);
            byte bi = b.GetElement(i);
            if (ai < bi) return -1;
            if (ai > bi) return 1;
        }

        return 0;

    }
}

public readonly struct ScheduledJob : IComparable<ScheduledJob>, IEquatable<ScheduledJob>
{
    public readonly static ScheduledJob Empty = new ();
    private readonly Vector128<byte> _jobDefinitionId;

    public static readonly ScheduledJob MinValue = new(Vector128<byte>.Zero, DateTime.MinValue);
    public static readonly ScheduledJob MaxValue = new(Vector128<byte>.AllBitsSet, DateTime.MaxValue);

    public static ScheduledJob Min(DateTime when) => new ScheduledJob(Vector128<byte>.Zero, when);
    public static ScheduledJob Max(DateTime when) => new ScheduledJob(Vector128<byte>.AllBitsSet, when);
    public ScheduledJob()
    {
        this._jobDefinitionId = Vector128<byte>.Zero;
        this.StartAt = DateTime.MinValue;
        
    }

    private ScheduledJob(Vector128<byte> jobDefinitionId, DateTime startAt)
    {
            _jobDefinitionId = jobDefinitionId;
            StartAt = startAt;
    }

    public ScheduledJob(Guid jobDefinitionId, DateTime startAt) : this(
        Unsafe.As<Guid, Vector128<byte>>(ref jobDefinitionId), startAt) { }
    public Guid JobDefinitionId
    {
        get
        {
            var tmp = _jobDefinitionId;
            return Unsafe.As<Vector128<byte>, Guid>(ref tmp);
        }
    }

    public DateTime StartAt { get; }



 

    public void Deconstruct(out Guid jobDefinitionId, out DateTime startAt)
    {
        jobDefinitionId = this.JobDefinitionId;
        startAt = this.StartAt;
    }
  
    public int CompareTo(ScheduledJob other)
    {
        var tmp = StartAt.CompareTo(other.StartAt);
        return tmp == 0 ? Vector128Comparer.Compare(this._jobDefinitionId, other._jobDefinitionId): tmp;
    }

    public bool Equals(ScheduledJob other)
    {
        return JobDefinitionId.Equals(other.JobDefinitionId) && Vector128.EqualsAll(this._jobDefinitionId, other._jobDefinitionId);
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