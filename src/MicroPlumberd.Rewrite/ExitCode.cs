namespace MicroPlumberd.Rewrite;

/// <summary>
/// The process exit codes. They are part of the tool's contract — an operator's shell script and CI branch on
/// them — so each one names a DIFFERENT recovery, and the store's state for each is stated here and in the
/// README.
/// </summary>
public enum ExitCode
{
    /// <summary>The rewrite completed and was verified; the container runs on the new data.</summary>
    Ok = 0,

    /// <summary>A guard refused: a sibling container is running, or a client is connected. Nothing changed.</summary>
    GuardRefusal = 1,

    /// <summary>The script does not parse. Nothing was started; nothing changed.</summary>
    ScriptError = 2,

    /// <summary>Docker is unreachable, the container does not exist, or the image could not be pulled. Nothing changed.</summary>
    DockerUnavailable = 3,

    /// <summary>The copy engine or the verification failed. The scratch store is removed; the OLD store is untouched.</summary>
    EngineFailure = 4,

    /// <summary>The filesystem swap failed. The original data directory has been restored and the container started.</summary>
    FilesystemFailure = 5
}
