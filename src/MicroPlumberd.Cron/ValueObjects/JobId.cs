using System.Diagnostics.CodeAnalysis;

namespace MicroPlumberd.Services.Cron;

/// <summary>
/// Represents a unique identifier for a job execution instance, combining the job definition ID and execution time.
/// </summary>
/// <param name="JobDefinitionId">The unique identifier of the job definition.</param>
/// <param name="At">The scheduled execution time for this job instance.</param>
public readonly record struct JobId(Guid JobDefinitionId, DateTime At) : IParsable<JobId>
{
    /// <summary>
    /// Converts the job ID to its string representation.
    /// </summary>
    /// <returns>A string in the format "{binary-time}/{guid}".</returns>
    public override string ToString()
    {
        return $"{At.ToBinary()}/{JobDefinitionId}";
    }

    /// <summary>
    /// Parses a string representation of a job ID.
    /// </summary>
    /// <param name="s">The string to parse.</param>
    /// <param name="provider">The format provider (not used).</param>
    /// <returns>The parsed job ID.</returns>
    public static JobId Parse(string s, IFormatProvider? provider)
    {
        var seg= s.Split('/');
        return new JobId(Guid.Parse(seg[1]), DateTime.FromBinary(long.Parse(seg[0])));
    }

    /// <summary>
    /// Attempts to parse a string representation of a job ID.
    /// </summary>
    /// <param name="s">The string to parse.</param>
    /// <param name="provider">The format provider (not used).</param>
    /// <param name="result">The parsed job ID if successful.</param>
    /// <returns>True if parsing succeeded; otherwise, false.</returns>
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