namespace MicroPlumberd.Rewrite;

/// <summary>What the operator asked the tool to do.</summary>
public enum RewriteMode
{
    /// <summary>Rewrite the store (the default).</summary>
    Rewrite,

    /// <summary>Swap a backup back in and restart the container.</summary>
    Rollback,

    /// <summary>Print the store's location, backups and guard results; change nothing.</summary>
    Status
}

/// <summary>
/// The parsed command line. <see cref="Program"/> builds one of these and does nothing else, so every test
/// drives <see cref="RewriteCommand.RunAsync"/> in-process with exactly the inputs a real invocation produces.
/// </summary>
public sealed record RewriteOptions
{
    /// <summary>Docker container id or name of the KurrentDB to rewrite.</summary>
    public required string Container { get; init; }

    /// <summary>Path to a <c>.js</c> rule script, or <c>null</c>.</summary>
    public string? ScriptPath { get; init; }

    /// <summary>Inline script source (<c>--eval</c>), or <c>null</c>.</summary>
    public string? Eval { get; init; }

    /// <summary>Copy nothing; report what a real run would do.</summary>
    public bool DryRun { get; init; }

    /// <summary>Do not pre-create the app's user projections on the new store.</summary>
    public bool NoProjectionCopy { get; init; }

    /// <summary>Skip the confirmation prompt.</summary>
    public bool Yes { get; init; }

    /// <summary>Allow rewriting a store on a named volume (swapped through a helper container).</summary>
    public bool ForceVolumeCopy { get; init; }

    /// <summary>Rewrite, rollback or status.</summary>
    public RewriteMode Mode { get; init; } = RewriteMode.Rewrite;

    /// <summary>For <see cref="RewriteMode.Rollback"/>: the backup directory to restore; <c>null</c> = the most recent.</summary>
    public string? BackupDir { get; init; }

    /// <summary>
    /// TEST HOOK. Throws immediately after the directory swap so the restore path can be exercised. Set from
    /// <c>MP_REWRITE_TEST_FAIL_AFTER_SWAP=1</c> by <see cref="Program"/>, or directly by a test.
    /// </summary>
    /// <remarks>
    /// It exists because the restore path is the one branch that only runs when something has already gone
    /// wrong — the least-travelled code in the tool, and the one an operator most needs to work.
    /// </remarks>
    public bool FailAfterSwapForTest { get; init; }

    /// <summary>Where the confirmation prompt reads its answer from. Defaults to standard input.</summary>
    public TextReader? ConfirmationInput { get; init; }

    /// <summary>Where the tool writes its plan and report. Defaults to standard output.</summary>
    public TextWriter? Output { get; init; }

    /// <summary>The script source, from <see cref="Eval"/> or <see cref="ScriptPath"/>; <c>null</c> for a pure copy.</summary>
    public string? ReadScriptSource()
    {
        if (Eval is not null && ScriptPath is not null)
            throw new ArgumentException("--script and --eval are mutually exclusive.");
        if (Eval is not null) return Eval;
        if (ScriptPath is null) return null;
        if (!File.Exists(ScriptPath))
            throw new FileNotFoundException($"Script file not found: {ScriptPath}", ScriptPath);
        return File.ReadAllText(ScriptPath);
    }
}
