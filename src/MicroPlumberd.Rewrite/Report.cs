using System.Text;
using MicroPlumberd.Migration;

namespace MicroPlumberd.Rewrite;

/// <summary>What the run did, in the form an operator reads before deciding whether to trust it.</summary>
public sealed record RewriteReport
{
    /// <summary>The exit code the process returns.</summary>
    public required ExitCode Code { get; init; }

    /// <summary>One line saying what happened, shown first.</summary>
    public required string Headline { get; init; }

    /// <summary>The container that was rewritten.</summary>
    public StoreContainer? Container { get; init; }

    /// <summary>True when nothing was written.</summary>
    public bool DryRun { get; init; }

    /// <summary>The migration id recorded in the new store's history.</summary>
    public string? MigrationId { get; init; }

    /// <summary>sha256 of the script text, or <c>null</c> for a pure copy.</summary>
    public string? ScriptChecksum { get; init; }

    /// <summary>The rules the run compiled to, in order.</summary>
    public IReadOnlyList<string> Descriptors { get; init; } = [];

    /// <summary>Source streams the rules dropped entirely, with their event counts.</summary>
    public IReadOnlyList<(string Stream, long Events)> DroppedStreams { get; init; } = [];

    /// <summary>Source streams the rules touched or copied, with source and destination counts.</summary>
    public IReadOnlyList<(string Stream, string Target, long Source, long Kept)> AffectedStreams { get; init; } = [];

    /// <summary>Events read, written, dropped and copied-verbatim-because-unreadable.</summary>
    public long SourceEvents { get; init; }

    /// <summary>Events written to the new store.</summary>
    public long Kept { get; init; }

    /// <summary>Events the rules dropped.</summary>
    public long Dropped { get; init; }

    /// <summary>Events whose payload could not be parsed and were copied byte-for-byte.</summary>
    public long UnparseableVerbatim { get; init; }

    /// <summary>User projections pre-created on the new store.</summary>
    public IReadOnlyList<string> CopiedProjections { get; init; } = [];

    /// <summary>Where the pre-rewrite store was kept.</summary>
    public string? BackupDir { get; init; }

    /// <summary>The verification report, when one ran.</summary>
    public VerificationReport? Verification { get; init; }

    /// <summary>How long the run took.</summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>Extra lines (guard results, status listing).</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];

    /// <summary>Renders the report.</summary>
    public string Format()
    {
        var sb = new StringBuilder();
        sb.AppendLine(Headline);
        if (Container is not null)
        {
            sb.AppendLine($"  container    : {Container.Name} ({Container.Image})");
            sb.AppendLine($"  data         : {Container.Data.StoreDir ?? "volume:" + Container.Data.VolumeName}");
        }
        if (DryRun) sb.AppendLine("  mode         : DRY RUN — nothing was written");
        if (MigrationId is not null) sb.AppendLine($"  migration    : {MigrationId}");
        if (ScriptChecksum is not null) sb.AppendLine($"  script sha256: {ScriptChecksum}");
        foreach (var d in Descriptors) sb.AppendLine($"  rule         : {d}");

        if (AffectedStreams.Count > 0)
        {
            sb.AppendLine($"  affected streams ({AffectedStreams.Count}):");
            foreach (var (s, t, src, kept) in AffectedStreams.OrderBy(x => x.Stream, StringComparer.Ordinal))
                sb.AppendLine(s == t
                    ? $"     {s}: {src} → {kept}"
                    : $"     {s} → {t}: {src} → {kept}");
        }
        if (DroppedStreams.Count > 0)
        {
            sb.AppendLine($"  dropped streams ({DroppedStreams.Count}):");
            foreach (var (s, n) in DroppedStreams.OrderBy(x => x.Stream, StringComparer.Ordinal))
                sb.AppendLine($"     {s} ({n} event(s))");
        }

        sb.AppendLine($"  events       : read {SourceEvents}, written {Kept}, dropped {Dropped}, "
                      + $"unreadable-copied-verbatim {UnparseableVerbatim}");
        if (CopiedProjections.Count > 0)
            sb.AppendLine($"  projections  : {string.Join(", ", CopiedProjections)}");
        if (BackupDir is not null) sb.AppendLine($"  backup       : {BackupDir}");
        foreach (var n in Notes) sb.AppendLine($"  {n}");
        if (Verification is not null) sb.Append(Verification.Format());
        sb.AppendLine($"  elapsed      : {Elapsed.TotalSeconds:0.0}s");
        sb.AppendLine($"  exit code    : {(int)Code} ({Code})");
        return sb.ToString();
    }
}
