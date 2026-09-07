using Microsoft.Extensions.Logging;

namespace MicroPlumberd.Rewrite;

/// <summary>
/// The filesystem half of the rewrite: create the new store's directory, swap it in by renaming within one
/// directory, and put the old one back if anything goes wrong.
/// </summary>
/// <remarks>
/// <para>Every rename happens INSIDE <see cref="DataLocation.Parent"/>, i.e. on one filesystem, so each is a
/// single atomic <c>rename(2)</c>. The swap as a whole is two of them and is therefore NOT atomic — the
/// window between them is precisely why <see cref="Restore"/> exists and why the backup is never deleted.</para>
/// <para><b>Ownership.</b> The KurrentDB image runs as uid 1001; an operator on a fleet host or the
/// <c>runner</c> user in CI does not. A rename needs write permission on the PARENT directory, not on the
/// directory being renamed, so the swap works regardless of who owns the data. What does not work by itself
/// is the container writing into a directory this tool created, so the new directory is made group/other
/// writable before the scratch store is pointed at it — and it keeps those permissions after the swap, which
/// is what lets the original container keep writing.</para>
/// </remarks>
public sealed class DirectorySwap(DataLocation data, ILogger logger)
{
    /// <summary>Marks a directory this tool created as the in-progress new store.</summary>
    public const string NewSuffix = ".new.";

    /// <summary>Marks the pre-rewrite store, kept forever.</summary>
    public const string BackupSuffix = ".bak.";

    /// <summary>Marks the rewritten store that a rollback displaced.</summary>
    public const string RolledBackSuffix = ".rolledback.";

    /// <summary>A UTC stamp that sorts lexicographically, used to name every directory this tool creates.</summary>
    public static string Stamp(DateTime utc) => utc.ToString("yyyyMMddTHHmmss");

    private string Path(string suffix, string stamp) =>
        System.IO.Path.Combine(data.Parent, data.Name + suffix + stamp);

    /// <summary>Creates the empty directory the scratch store will write the rewritten history into.</summary>
    public string CreateNewStoreDir(string stamp)
    {
        var dir = Path(NewSuffix, stamp);
        Directory.CreateDirectory(dir);
        MakeContainerWritable(dir);
        logger.LogInformation("Created new-store directory {Dir}.", dir);
        return dir;
    }

    /// <summary>
    /// Grants the container's user write access to a directory this tool created.
    /// </summary>
    /// <remarks>
    /// The container runs as a uid the tool has no way to become and often cannot even <c>chown</c> to (that
    /// needs root). Widening the mode is the portable option that works for a non-root operator on a fleet
    /// host and for the <c>runner</c> user on <c>ubuntu-latest</c> alike. It is only ever applied to a
    /// directory this tool just created, never to the operator's existing data.
    /// </remarks>
    public static void MakeContainerWritable(string dir)
    {
        if (OperatingSystem.IsWindows()) return;
        File.SetUnixFileMode(dir,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute);
    }

    /// <summary>
    /// Swaps the new store in: <c>data → data.bak.&lt;ts&gt;</c> then <c>data.new.&lt;ts&gt; → data</c>.
    /// Returns the backup path. Both containers must already be stopped.
    /// </summary>
    public SwapResult Swap(string newStoreDir, string stamp)
    {
        var store = data.StoreDir!;
        var backup = Path(BackupSuffix, stamp);

        Directory.Move(store, backup);
        logger.LogInformation("Renamed {Store} → {Backup}.", store, backup);
        try
        {
            Directory.Move(newStoreDir, store);
        }
        catch
        {
            // The half-swapped state is the only one an operator cannot reason about, so it never survives
            // this method: put the original back before the exception leaves.
            Directory.Move(backup, store);
            logger.LogError("Second rename failed; restored {Backup} → {Store}.", backup, store);
            throw;
        }
        logger.LogInformation("Renamed {New} → {Store}.", newStoreDir, store);
        return new SwapResult(backup, newStoreDir);
    }

    /// <summary>Reverses a <see cref="Swap"/>: puts the rewritten store back under its <c>.new.</c> name and the backup back.</summary>
    public void Restore(SwapResult swap)
    {
        var store = data.StoreDir!;
        if (Directory.Exists(store) && !Directory.Exists(swap.NewStoreDir))
            Directory.Move(store, swap.NewStoreDir);
        if (!Directory.Exists(store) && Directory.Exists(swap.BackupDir))
            Directory.Move(swap.BackupDir, store);
        logger.LogWarning("Restored the original store: {Backup} → {Store}.", swap.BackupDir, store);
    }

    /// <summary>Backup directories present, most recent first.</summary>
    public IReadOnlyList<string> Backups() => Find(BackupSuffix);

    private IReadOnlyList<string> Find(string suffix)
    {
        if (!Directory.Exists(data.Parent)) return [];
        var prefix = data.Name + suffix;
        return Directory.EnumerateDirectories(data.Parent)
            .Where(d => System.IO.Path.GetFileName(d).StartsWith(prefix, StringComparison.Ordinal))
            .OrderByDescending(d => d, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Swaps a backup back in: the current store becomes <c>data.rolledback.&lt;ts&gt;</c> and the chosen
    /// backup becomes <c>data</c>. The container must already be stopped.
    /// </summary>
    public RollbackResult Rollback(string? backupDir, string stamp)
    {
        var chosen = backupDir ?? Backups().FirstOrDefault()
            ?? throw new RewriteRefusedException(ExitCode.FilesystemFailure,
                $"No backup to roll back to: nothing matching '{data.Name}{BackupSuffix}*' in {data.Parent}.");
        if (!Directory.Exists(chosen))
            throw new RewriteRefusedException(ExitCode.FilesystemFailure, $"Backup not found: {chosen}");

        var store = data.StoreDir!;
        var displaced = Path(RolledBackSuffix, stamp);
        Directory.Move(store, displaced);
        try
        {
            Directory.Move(chosen, store);
        }
        catch
        {
            Directory.Move(displaced, store);
            throw;
        }
        logger.LogWarning("Rolled back: {Chosen} → {Store}; the rewritten store is kept at {Displaced}.",
            chosen, store, displaced);
        return new RollbackResult(chosen, displaced);
    }
}

/// <summary>Where the swap put things, so it can be undone.</summary>
/// <param name="BackupDir">The pre-rewrite store.</param>
/// <param name="NewStoreDir">The name the rewritten store had before it became the live one.</param>
public sealed record SwapResult(string BackupDir, string NewStoreDir);

/// <summary>Where a rollback put things.</summary>
/// <param name="RestoredFrom">The backup that is now live.</param>
/// <param name="DisplacedTo">Where the rewritten store was moved to.</param>
public sealed record RollbackResult(string RestoredFrom, string DisplacedTo);
