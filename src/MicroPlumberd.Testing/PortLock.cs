namespace MicroPlumberd.Testing;

/// <summary>
/// A store port held MACHINE-WIDE for the store's lifetime (card microplumberd-testing-store-port-race). Every process's
/// PortSearcher starts at the same port, and a free-port probe only sees a port as taken once its container has STARTED —
/// seconds later — so two processes chose the same port. A lock file per port, opened exclusively (on Unix .NET takes an
/// advisory flock for FileShare.None), is held until the store is disposed and released by the OS if the process dies.
/// <para>
/// The directory is shared by every USER of the machine (rev-reach 9de418b9d m1): it is made 1777 (sticky) and each lock
/// file 0666, after creation, whatever the creator's umask. A lock file this user cannot open (another user's, made before
/// these modes) is a port to skip; a directory in which nothing can be locked is refused, named — never an unhandled throw
/// and never an endless search.
/// </para>
/// </summary>
internal sealed class PortLock : IDisposable
{
    /// <summary>One directory for every process on the machine (not the per-user/per-process TMPDIR).</summary>
    internal static readonly string DefaultDirectory = OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "microplumberd-testing-ports")
        : "/tmp/microplumberd-testing-ports";

    /// <summary>Consecutive ports this user may not lock before the directory itself is judged unusable.</summary>
    internal const int DeniedLimit = 20;

    private const UnixFileMode SharedDirectory = (UnixFileMode)0b1_111_111_111;   // 1777: sticky, rwx for all
    private const UnixFileMode SharedFile = (UnixFileMode)0b110_110_110;          // 0666

    private readonly FileStream _held;
    private PortLock(FileStream held) => _held = held;

    /// <summary>The next port that no other process holds AND that is free to bind, with its lock held.</summary>
    internal static (int Port, PortLock Lock) Acquire(PortSearcher searcher, string? directory = null)
    {
        directory ??= DefaultDirectory;
        // Shared modes on the directory only when THIS process creates it: an existing one is used as it is — another
        // user's cannot be chmodded, and one we cannot use is refused below, named, with the chmod that fixes it.
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
            TryChmod(() => File.SetUnixFileMode(directory, SharedDirectory));
        }

        var denied = 0;
        while (true)
        {
            var port = searcher.FindNextAvailablePort();
            switch (TryHold(directory, port))
            {
                case Held { Lock: var held }: return (port, held);
                case Denied when ++denied >= DeniedLimit:
                    throw new InvalidOperationException(
                        $"No store port could be locked in '{directory}': this user may not create or open lock files there " +
                        $"({DeniedLimit} ports in a row). Make it shared — chmod 1777 '{directory}' — or remove it so it is recreated.");
                case Denied: continue;
                default: denied = 0; continue; // held by another process: the next port
            }
        }
    }

    internal abstract record Outcome;
    internal sealed record Held(PortLock Lock) : Outcome;
    internal sealed record Taken : Outcome;
    internal sealed record Denied : Outcome;

    internal static Outcome TryHold(string directory, int port)
    {
        var path = Path.Combine(directory, $"{port}.lock");
        try
        {
            var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            TryChmod(() => File.SetUnixFileMode(path, SharedFile));
            return new Held(new PortLock(stream));
        }
        catch (UnauthorizedAccessException)
        {
            return new Denied(); // another user's lock file, or a directory this user may not write
        }
        catch (IOException)
        {
            return new Taken(); // another process holds this port
        }
    }

    /// <summary>Best effort: a path another user owns cannot be chmodded by this one — it is then used as it is.</summary>
    private static void TryChmod(Action chmod)
    {
        if (OperatingSystem.IsWindows()) return;
        try { chmod(); }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }

    public void Dispose() => _held.Dispose();
}
