using System.Diagnostics.CodeAnalysis;

namespace MicroPlumberd.Services.Cron;

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