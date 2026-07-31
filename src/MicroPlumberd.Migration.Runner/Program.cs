using System.Reflection;
using KurrentDB.Client;
using Microsoft.Extensions.Logging;
using MicroPlumberd.Migration;

// mp-migrate --source <conn> --dest <conn> [--dry-run]
//
// Offline event-store rewrite. SOURCE is read-only; DEST must be a fresh, empty store (topology is
// provided externally — e.g. source ES on the existing volume RO + dest ES on a fresh volume, both up).

var args0 = ParseArgs(args);
if (!args0.TryGetValue("source", out var sourceConn) || !args0.TryGetValue("dest", out var destConn))
{
    Console.Error.WriteLine("Usage: mp-migrate --source <connection-string> --dest <connection-string> [--dry-run]");
    Console.Error.WriteLine("  --source   SOURCE KurrentDB connection string (read-only).");
    Console.Error.WriteLine("  --dest     DEST KurrentDB connection string (fresh, empty store).");
    Console.Error.WriteLine("  --dry-run  Report per-migration counts; write nothing.");
    Console.Error.WriteLine();
    Console.Error.WriteLine("REQUIREMENT: the DEST EventStore MUST run with standard projections enabled");
    Console.Error.WriteLine("($by_event_type running). The tool PRE-CREATES the app's [OutputStream] join projections");
    Console.Error.WriteLine("on the fresh dest and PACES the event copy (per-event link emission) so those merge");
    Console.Error.WriteLine("streams are rebuilt in commit/arrival order; the app NO-OPs on boot (mp_query_hash match).");
    Console.Error.WriteLine("If standard projections are off, $et never repopulates and the join/output streams stay empty.");
    return 2;
}

var dryRun = args0.ContainsKey("dry-run");

using var loggerFactory = LoggerFactory.Create(b => b
    .SetMinimumLevel(LogLevel.Information)
    .AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; }));
var log = loggerFactory.CreateLogger("mp-migrate");

var sourceSettings = KurrentDBClientSettings.Create(sourceConn);
var destSettings = KurrentDBClientSettings.Create(destConn);
await using var source = new KurrentDBClient(sourceSettings);
await using var dest = new KurrentDBClient(destSettings);
var sourceProjections = new KurrentDBProjectionManagementClient(sourceSettings);
var destProjections = new KurrentDBProjectionManagementClient(destSettings);

// PACED PROJECTION COPY (default on a real migration): pre-create the source's [OutputStream] join projections
// on the fresh dest and pace the copy per-event so each merge stream is rebuilt in commit order. On --dry-run
// the runner respects dryRun end-to-end (pre-creates nothing, no pacing) and reports what WOULD be created.
var projectionCopy = new ProjectionCopyContext
{
    SourceProjections = sourceProjections,
    DestProjections = destProjections,
    SourceConnectionString = sourceConn
};

var migrations = MigrationDiscovery.FromAssemblies(Assembly.GetExecutingAssembly());
log.LogInformation("Discovered {Count} migration(s): {Ids}", migrations.Count,
    string.Join(", ", migrations.Select(m => m.Id)));
log.LogWarning("DEST must run with standard projections enabled ($by_event_type). The tool pre-creates the app's "
               + "[OutputStream] join projections on the dest and PACES the copy (per-event link emission) so the "
               + "merge streams rebuild in commit order; with projections off they stay empty.");

try
{
    var result = await new MigrationRunner(loggerFactory)
        .RunAsync(source, dest, migrations, dryRun, projectionCopy);

    Console.WriteLine();
    Console.WriteLine(dryRun ? "=== DRY RUN (nothing written) ===" : "=== MIGRATION COMPLETE ===");
    Console.WriteLine($"Pending applied: {(result.PendingMigrationIds.Count == 0 ? "(none)" : string.Join(", ", result.PendingMigrationIds))}");
    Console.WriteLine($"Source events scanned: {result.Copy.SourceEvents}");
    Console.WriteLine($"Kept: {result.Copy.Kept}   Dropped: {result.Copy.Dropped}");
    Console.WriteLine($"Merge/link events skipped (source $> links, rebuilt by the paced dest projections): {result.Copy.LinkEventsSkipped}");
    if (result.CopiedProjections.Count > 0)
        Console.WriteLine($"Projections {(dryRun ? "that WOULD be pre-created" : "pre-created + paced on the dest")}: {string.Join(", ", result.CopiedProjections)}");
    Console.WriteLine("Per-migration:");
    foreach (var id in result.PendingMigrationIds)
    {
        var s = result.Copy.MigrationStats[id];
        Console.WriteLine($"  {id}: dropped={s.Dropped}, renamed={s.Renamed}, transformed={s.Transformed}");
    }

    // Unparseable-but-declared-JSON payloads are copied VERBATIM (byte-for-byte), never dropped — a benign
    // warning, not data loss, so it does NOT fail the run.
    if (result.Copy.UnparseableVerbatim > 0)
        Console.WriteLine($"NOTE: {result.Copy.UnparseableVerbatim} source event(s) had unparseable JSON payloads "
                          + "and were copied VERBATIM (byte-for-byte). No data loss.");

    if (result.Verification is not null)
    {
        Console.WriteLine();
        Console.WriteLine(result.Verification.Format());
        return result.Verification.AllOk ? 0 : 1;
    }
    return 0;
}
catch (MigrationChecksumMismatchException ex)
{
    log.LogError(ex, "Refusing to run: an already-applied migration changed.");
    return 3;
}

static Dictionary<string, string> ParseArgs(string[] argv)
{
    var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < argv.Length; i++)
    {
        if (!argv[i].StartsWith("--", StringComparison.Ordinal)) continue;
        var key = argv[i][2..];
        if (i + 1 < argv.Length && !argv[i + 1].StartsWith("--", StringComparison.Ordinal))
            d[key] = argv[++i];
        else
            d[key] = "true"; // flag
    }
    return d;
}
