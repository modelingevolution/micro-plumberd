using FluentAssertions;
using MicroPlumberd.Testing;

namespace MicroPlumberd.Tests.Unit;

/// <summary>
/// rev-reach 9de418b9d m1: the port-lock directory is shared by every USER of a machine. Each row uses its OWN directory
/// (the default is shared with every parallel class's stores), and its own port range.
/// </summary>
public class PortLockTests : IDisposable
{
    private readonly string _dir = Path.Combine(Directory.CreateTempSubdirectory("mp-portlock-").FullName, "ports");

    public void Dispose()
    {
        var root = Path.GetDirectoryName(_dir)!;
        if (!OperatingSystem.IsWindows() && Directory.Exists(_dir)) File.SetUnixFileMode(_dir, (UnixFileMode)0b111_111_111);
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void TheDirectoryAndItsLockFiles_AreSharedWithEveryUser_WhateverTheUmask()
    {
        if (OperatingSystem.IsWindows()) return;
        var (port, held) = PortLock.Acquire(new PortSearcher(41_000), _dir);
        using (held)
        {
            File.GetUnixFileMode(_dir).Should().Be((UnixFileMode)0b1_111_111_111, "1777: any user may add a lock, none may remove another's");
            File.GetUnixFileMode(Path.Combine(_dir, $"{port}.lock")).Should().Be((UnixFileMode)0b110_110_110, "0666: any user may lock it later");
        }
    }

    [Fact]
    public void ALockFileThisUserMayNotOpen_IsAPortToSkip_NotAThrow()
    {
        if (OperatingSystem.IsWindows()) return;
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "42001.lock"), "");
        File.SetUnixFileMode(Path.Combine(_dir, "42001.lock"), UnixFileMode.UserRead); // as another user's 0644 file is to this one

        var (port, held) = PortLock.Acquire(new PortSearcher(42_000), _dir);
        using (held) port.Should().BeGreaterThan(42_001, "the port whose lock file this user may not open is skipped");
    }

    [Fact]
    public void ADirectoryInWhichNothingCanBeLocked_IsRefusedNamed_NeverAnUnhandledThrow()
    {
        if (OperatingSystem.IsWindows()) return;
        Directory.CreateDirectory(_dir);
        File.SetUnixFileMode(_dir, (UnixFileMode)0b101_101_101); // 0555 — as another user's 0755 directory is to this one

        var acquire = () => PortLock.Acquire(new PortSearcher(43_000), _dir);

        acquire.Should().Throw<InvalidOperationException>().WithMessage($"*'{_dir}'*chmod 1777*");
    }
}
