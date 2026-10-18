using System.Net;
using System.Net.Sockets;

namespace MicroPlumberd.Testing;

/// <summary>
/// A store port held MACHINE-WIDE for the store's lifetime (card microplumberd-testing-store-port-race). Every process's
/// PortSearcher starts at the same port, and a free-port probe only sees a port as taken once its container has STARTED —
/// seconds later — so two processes chose the same port. A lock file per port, opened exclusively (on Unix .NET takes an
/// advisory flock for FileShare.None), is held until the store is disposed and released by the OS if the process dies.
/// </summary>
internal sealed class PortLock : IDisposable
{
    /// <summary>One directory for every process on the machine (not the per-user/per-process TMPDIR).</summary>
    internal static string Directory { get; set; } = OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "microplumberd-testing-ports")
        : "/tmp/microplumberd-testing-ports";

    private readonly FileStream _held;
    private PortLock(FileStream held) => _held = held;

    /// <summary>The next port that no other process holds AND that is free to bind, with its lock held.</summary>
    internal static (int Port, PortLock Lock) Acquire(PortSearcher searcher)
    {
        System.IO.Directory.CreateDirectory(Directory);
        while (true)
        {
            var port = searcher.FindNextAvailablePort();
            var held = TryHold(port);
            if (held != null) return (port, held);
        }
    }

    internal static PortLock? TryHold(int port)
    {
        try
        {
            var stream = new FileStream(Path.Combine(Directory, $"{port}.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return new PortLock(stream);
        }
        catch (IOException)
        {
            return null; // another process holds this port
        }
    }

    public void Dispose() => _held.Dispose();
}
