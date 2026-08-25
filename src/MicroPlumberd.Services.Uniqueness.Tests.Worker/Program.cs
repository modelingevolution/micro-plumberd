using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MicroPlumberd.Services.Uniqueness;

// A REAL OS process that reserves a name through the shipped library (UQ-SQLITE-01).
//
// This exists because threads cannot test what the shared-volume SQLite deployment rests on. Threads in
// one process share a connection pool and an in-process lock manager; N containers on a shared volume
// share NEITHER. Only separate processes exercise cross-process file locking, so this is a separate
// executable rather than a Task.
//
// Usage: <connectionString> <name> <sourceGuid> <startAtUtcTicks>
// Exit codes are the test's assertion surface — see UqExit.

if (args.Length != 4)
{
    Console.Error.WriteLine("usage: <connectionString> <name> <sourceGuid> <startAtUtcTicks>");
    return UqExit.BadUsage;
}

var (connectionString, name, source, startAt) =
    (args[0], args[1], Guid.Parse(args[2]), new DateTime(long.Parse(args[3]), DateTimeKind.Utc));

var builder = Host.CreateApplicationBuilder();
builder.Services.AddLogging(l => l.ClearProviders());
// ensureSchema:false — the test creates the table once up front. This process is here to race the
// INSERT, not the DDL.
builder.Services.AddUniquenessSqlite<CompanyNip>(connectionString, ensureSchema: false);

using var host = builder.Build();
await host.StartAsync();

var uq = host.Services.GetRequiredService<IUniqueNameReservation<CompanyNip>>();

// All processes are released at the same instant, so they genuinely contend. Without this the spawn
// cost (tens of ms each) would serialise them and the "race" would prove nothing.
var wait = startAt - DateTime.UtcNow;
if (wait > TimeSpan.Zero) await Task.Delay(wait);
while (DateTime.UtcNow < startAt) { /* tighten the last moment */ }

try
{
    await uq.Reserve(name, source);
    Console.Out.WriteLine($"WON {source}");
    return UqExit.Won;
}
catch (UniqueNameConflictException ex)
{
    // The correct way to lose: someone else holds the name and we were told who.
    Console.Out.WriteLine($"CONFLICT heldBy={ex.HeldBy}");
    return UqExit.Conflict;
}
catch (Microsoft.Data.Sqlite.SqliteException ex)
{
    // The failure mode the deployment must NOT exhibit: contention surfacing as SQLITE_BUSY (5) /
    // SQLITE_LOCKED (6) instead of waiting. Reported distinctly so the test can tell them apart.
    Console.Error.WriteLine($"SQLITE {ex.SqliteErrorCode}/{ex.SqliteExtendedErrorCode}: {ex.Message}");
    return UqExit.SqliteError;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"OTHER {ex.GetType().Name}: {ex.Message}");
    return UqExit.OtherError;
}

/// <summary>Exit codes: the contract between this worker and the test that spawns it.</summary>
static class UqExit
{
    public const int Won = 0;
    public const int Conflict = 3;
    public const int SqliteError = 4;   // SQLITE_BUSY and friends — a failure, never acceptable
    public const int OtherError = 5;
    public const int BadUsage = 64;
}

/// <summary>Must match the test's category type NAME — that is what names the table.</summary>
record CompanyNip;
