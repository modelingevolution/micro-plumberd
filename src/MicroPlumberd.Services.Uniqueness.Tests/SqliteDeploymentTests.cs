using System.Diagnostics;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MicroPlumberd.Services.Uniqueness.Tests;

/// <summary>
/// SQLite as a SUPPORTED deployment: several processes on one shared volume. These are the only tests
/// that exercise CROSS-PROCESS file locking — every other concurrency test in this suite is
/// single-process/multi-threaded, sharing one connection pool and one in-process lock manager, which
/// separate containers do NOT share.
/// </summary>
/// <remarks>
/// MEASURED 2026-07-16 — the unique index IS load-bearing across processes: downgraded to a plain
/// index, separate processes produce 2-4 winners for one name. But detection is PROBABILISTIC (~75%
/// per race): sometimes the processes queue on SQLite's single write lock, each re-reads the committed
/// row and reports a conflict, and the INSERTs never contend — a pass that proves nothing. Widening the
/// release window does not fix it (8s measured no better than 3s); it is scheduling, not startup skew.
/// Hence ROUNDS, exactly as UQ-CONC-01 does: a race that passes once proves nothing.
/// </remarks>
[Trait("Category", "Integration")]
public class SqliteDeploymentTests
{
    const string Nip = "7151960049";
    const int Racers = 16;

    /// <summary>A single race detects a missing lock only ~75% of the time (measured), so repeat.
    /// 3 rounds leaves ~1.6% chance of missing it; 1 round would leave 25%.</summary>
    const int Rounds = 3;

    [Fact] // UQ-SQLITE-01 — uniqueness holds across SEPARATE PROCESSES on one shared file
    public async Task Uq_Sqlite_01_uniqueness_holds_across_separate_processes()
    {
        await using var h = await UqHarness.Create(new SqliteUqProvider());
        await h.Start(s => h.Provider.Register<CompanyNip>(s));   // create the schema once, up front

        var worker = WorkerPath();
        for (var round = 0; round < Rounds; round++)
            await OneRace(h, worker, $"{Nip}-{round}", round);
    }

    static async Task OneRace(UqHarness h, string worker, string name, int round)
    {
        var sources = Enumerable.Range(0, Racers).Select(_ => Guid.NewGuid()).ToArray();

        // Release every process at one instant, once they have all booted (host + EF model + first
        // connection). 3s is enough for that; a longer window measured no better, because what is left
        // is OS scheduling, not startup skew. Rounds — not a bigger window — are what make this reliable.
        var startAt = DateTime.UtcNow.AddSeconds(3);

        var procs = sources.Select(src => Run(worker,
            h.Provider.ConnectionString, name, src.ToString(), startAt.Ticks.ToString())).ToArray();
        var results = await Task.WhenAll(procs);

        var busy = results.Where(r => r.Exit == 4).ToArray();
        busy.Should().BeEmpty(
            "contention between processes must WAIT (busy_timeout) and be arbitrated by the unique " +
            "index, never surface as SQLITE_BUSY. Got: " + string.Join(" | ", busy.Select(x => x.Err)));

        var other = results.Where(r => r.Exit is not (0 or 3)).ToArray();
        other.Should().BeEmpty("no process may fail for any other reason. Got: " +
                               string.Join(" | ", other.Select(x => $"exit {x.Exit}: {x.Err}")));

        results.Count(r => r.Exit == 0).Should().Be(1, $"exactly one process may win the name (round {round})");
        results.Count(r => r.Exit == 3).Should().Be(Racers - 1, "every loser must be told it is a conflict");

        var rows = await h.Provider.RowsFor(nameof(CompanyNip), name);
        rows.Should().ContainSingle("exactly one row may exist for the name across all processes");

        var winner = Guid.Parse(results.Single(r => r.Exit == 0).Out.Replace("WON ", "").Trim());
        rows.Single().SourceId.Should().Be(winner, "the surviving row must be the winner's");

        // Every loser must identify the REAL holder — across a process boundary, where it cannot have
        // seen the winner in memory and can only have learned it by re-reading the shared file.
        // This is what makes the scenario bite: see the note on the class about the unique index.
        var heldBy = results.Where(r => r.Exit == 3)
            .Select(r => r.Out.Replace("CONFLICT heldBy=", "").Trim()).ToArray();
        heldBy.Should().OnlyContain(x => x == winner.ToString(),
            "each of the 15 losing PROCESSES must report the winner as the holder, never Guid.Empty — " +
            "it can only know that by re-reading the shared file");
    }

    [Fact] // UQ-SQLITE-02 — WAL is on and ENFORCED; busy_timeout is set [structural only]
    public async Task Uq_Sqlite_02_wal_is_on_and_busy_timeout_is_set()
    {
        await using var h = await UqHarness.Create(new SqliteUqProvider());
        var host = await h.Start(s => h.Provider.Register<CompanyNip>(s));

        // STRUCTURAL, not behavioural, and deliberately so. The obvious assertion — "under contention
        // nobody fails with SQLITE_BUSY" — is a FALSE GUARD: it passes at busy_timeout=0, because
        // Microsoft.Data.Sqlite retries SQLITE_BUSY at the COMMAND level via `Default Timeout` (30s),
        // an ADO-layer retry that is no part of SQLite's contract. MEASURED: busy_timeout=0 with the
        // default timeout SUCCEEDED after 3184ms. A behavioural test cannot see a missing pragma —
        // the same lesson as the non-unique index that left 27 of 28 tests green.

        // journal_mode is safe to read on ANY connection: it is PERSISTENT IN THE FILE.
        await using (var raw = new SqliteConnection(h.Provider.ConnectionString))
        {
            await raw.OpenAsync();
            await using var cmd = raw.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode";
            (await cmd.ExecuteScalarAsync())?.ToString().Should().Be("wal",
                "the shared-volume deployment needs WAL so readers and a writer do not exclude each " +
                "other; it also makes the same-host boundary STRUCTURAL, because WAL's shared memory " +
                "(-shm) cannot work over a network filesystem, so such a mount fails loudly instead " +
                "of corrupting silently");
        }

        // busy_timeout is PER-CONNECTION, so it MUST be read on a connection EF itself opened — the
        // only one the interceptor configured. A raw SqliteConnection is pool-dependent: it may draw a
        // handle EF already configured (reads 5000 — a false PASS) or a fresh one (reads 0 — a false
        // FAIL). Reaching EF's connection needs reflection because UniquenessDb<T> is internal; that is
        // ugly, but the alternative is an assertion that passes or fails for reasons unrelated to the fix.
        var busyTimeout = await BusyTimeoutOnEfsOwnConnection(host);
        busyTimeout.Should().Be(5000,
            "the wait must be SQLite's own busy handler, set explicitly by this registration — not an " +
            "ADO-layer command retry inherited from an unrelated `Default Timeout`, which is not part " +
            "of SQLite's contract and vanishes if a caller lowers that timeout");
    }

    [Fact] // UQ-SQLITE-02 — a REFUSED WAL switch THROWS, naming the network-filesystem cause
    public async Task Uq_Sqlite_02_refused_wal_switch_throws()
    {
        // A REAL refusal, not a mock. SQLite's unix-dotfile VFS has no shared-memory support, which is
        // exactly what a network filesystem (NFS/SMB) lacks — so WAL cannot be enabled. SQLite signals
        // that by LEAVING THE JOURNAL MODE ALONE rather than erroring: measured here, PRAGMA
        // journal_mode=WAL returns 'delete' and does not throw. That silent success is the whole reason
        // the interceptor reads the mode back, and it is what stands between us and a working-looking
        // database whose cross-host locking is unreliable — where the failure mode is CORRUPTION.
        var file = Path.Combine(Path.GetTempPath(), $"uq_wal_{Guid.NewGuid():N}.db");
        try
        {
            var builder = Host.CreateApplicationBuilder();
            builder.Services.AddLogging(l => l.ClearProviders());
            builder.Services.AddUniquenessSqlite<CompanyNip>($"Data Source=file:{file}?vfs=unix-dotfile");

            using var host = builder.Build();
            var thrown = await Record.ExceptionAsync(() => host.StartAsync());

            thrown.Should().NotBeNull(
                "a database that cannot run WAL must be REFUSED, not run with unreliable locking");
            var message = Flatten(thrown!);
            message.Should().Contain("WAL", "the error must say what was refused");
            message.Should().Contain("NETWORK", "the error must name the usual cause so it is actionable");
            message.Should().Contain("Postgres", "the error must name the way out for a multi-host deployment");
        }
        finally
        {
            try { File.Delete(file); } catch { /* temp file */ }
        }
    }

    static string Flatten(Exception ex)
    {
        var sb = new System.Text.StringBuilder();
        for (Exception? e = ex; e is not null; e = e.InnerException) sb.AppendLine(e.Message);
        return sb.ToString();
    }

    /// <summary>
    /// Reads PRAGMA busy_timeout on a connection EF ITSELF opened — the only connection the interceptor
    /// ran on. Not a raw SqliteConnection: Microsoft.Data.Sqlite pools handles, so a raw one may draw a
    /// handle EF already configured (reads 5000 — a false PASS) or a fresh one (reads 0 — a false FAIL),
    /// measuring the pool rather than the fix.
    /// </summary>
    /// <remarks>
    /// busy_timeout is PER-CONNECTION, so it is read while EF's connection is still OPEN. Dispose the
    /// context first and the handle returns to the pool — and the next read measures the pool again.
    /// Reaching UniquenessDb&lt;T&gt; needs InternalsVisibleTo; it replaced reflection on a type-name
    /// string, which could not fail at compile time.
    /// </remarks>
    static async Task<int> BusyTimeoutOnEfsOwnConnection(IHost host)
    {
        var factory = host.Services.GetRequiredService<IDbContextFactory<UniquenessDb<CompanyNip>>>();
        await using var ctx = await factory.CreateDbContextAsync();

        await ctx.Database.OpenConnectionAsync();          // <- the interceptor runs HERE
        // DO NOT move this read outside the `await using` above, and do not dispose the context first.
        // busy_timeout is PER-CONNECTION state: once the context is disposed the handle goes back to the
        // pool, and re-reading it measures the POOL instead of the interceptor — which is the exact bug
        // this method was written to fix, and it would go unnoticed because the value read from a pooled
        // handle is usually still 5000.
        var conn = ctx.Database.GetDbConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA busy_timeout";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    static async Task<(int Exit, string Out, string Err)> Run(string worker, params string[] args)
    {
        var psi = new ProcessStartInfo("/home/rmaciag/.dotnet/dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(worker);
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        return (p.ExitCode, await stdout, await stderr);
    }

    /// <summary>The worker executable, built alongside the tests. Fails loudly rather than silently
    /// skipping — an unfindable worker must not look like a pass.</summary>
    static string WorkerPath()
    {
        var here = AppContext.BaseDirectory;                       // .../Tests/bin/<cfg>/net10.0/
        var cfgDir = new DirectoryInfo(here).Parent!.Parent!;       // .../bin/<cfg> -> .../bin
        var config = new DirectoryInfo(here).Parent!.Name;          // Debug | Release
        var src = cfgDir.Parent!.Parent!;                           // .../src
        var dll = Path.Combine(src.FullName, "MicroPlumberd.Services.Uniqueness.Tests.Worker",
            "bin", config, "net10.0", "MicroPlumberd.Services.Uniqueness.Tests.Worker.dll");

        if (!File.Exists(dll))
            throw new FileNotFoundException(
                $"UQ-SQLITE-01 worker not found at '{dll}'. It must be built for this scenario to mean " +
                "anything — threads cannot substitute for real processes here.", dll);
        return dll;
    }
}
