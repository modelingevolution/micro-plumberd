using System.Diagnostics;
using System.Reflection;
using FluentAssertions;
using MicroPlumberd.Tests.Utils;
using Xunit.Abstractions;

namespace MicroPlumberd.Tests.Integration;

/// <summary>
/// card microplumberd-testing-store-port-race: two test PROCESSES on one host (two engineers' suites) each start
/// EventStoreServers. Every process's PortSearcher began at 2701 and a port only counted as taken once its container
/// STARTED; containers are named eventstore-mem-&lt;port&gt; and found BY NAME — so the second process restarted the first's
/// in-memory store (wiping it) or removed it on cleanup ("connection refused"; Docker 409 "removal already in progress" was
/// observed 2026-10-18 13:52Z when two suites overlapped). Two StoreHolder processes start their stores at ONE barrier
/// instant; each must keep its own store, on its own port, in its own container, for the whole hold.
/// </summary>
[TestCategory("Integration")]
public class StorePortRaceTests(ITestOutputHelper output)
{
    private static readonly string Holder = typeof(StorePortRaceTests).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "StoreHolderDll").Value!;

    [Fact]
    public async Task TwoProcessesStartingStoresAtOnce_EachKeepTheirOwnStore()
    {
        File.Exists(Holder).Should().BeTrue($"the StoreHolder child is built beside the tests ({Holder})");
        var barrier = DateTime.UtcNow.AddSeconds(5).Ticks;

        var children = Enumerable.Range(0, 2).Select(_ => Run(barrier, holdSeconds: 20)).ToArray();
        var results = await Task.WhenAll(children);
        foreach (var r in results) output.WriteLine($"exit={r.Exit} {r.Line}");

        results.Should().OnlyContain(r => r.Exit == 0 && r.Line.EndsWith("RESULT=SURVIVED"), "each process keeps its own store");
        results.Select(r => Field(r.Line, "PORT")).Should().OnlyHaveUniqueItems("two processes never share a port");
        results.Select(r => Field(r.Line, "CONTAINER")).Should().OnlyHaveUniqueItems("nor a container");
    }

    private static string Field(string line, string key) =>
        line.Split(' ').Single(f => f.StartsWith(key + "=", StringComparison.Ordinal))[(key.Length + 1)..];

    private static async Task<(int Exit, string Line)> Run(long barrierTicks, int holdSeconds)
    {
        var psi = new ProcessStartInfo("dotnet", [Holder, barrierTicks.ToString(), holdSeconds.ToString()])
        { RedirectStandardOutput = true, RedirectStandardError = true };
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var bound = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try { await process.WaitForExitAsync(bound.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); return (-1, $"TIMEOUT stderr={await stderr}"); }
        var line = (await stdout).Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() ?? $"no output; stderr={await stderr}";
        return (process.ExitCode, line);
    }
}
