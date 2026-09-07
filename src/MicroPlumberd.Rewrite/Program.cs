using Docker.DotNet;
using MicroPlumberd.Rewrite;
using Microsoft.Extensions.Logging;

// Program parses arguments and NOTHING else — every behaviour lives in RewriteCommand, which the end-to-end
// suite calls in-process, so the tested code and the shipped code are the same code.

const string Usage = """
mp-rewrite <container> [--script <file.js>] [--eval "<js>"] [--dry-run]
                       [--no-projection-copy] [--yes] [--user <u>] [--password <p>]
mp-rewrite <container> --rollback [<backup-dir>]
mp-rewrite <container> --status

Exit codes: 0 ok · 1 guard refusal · 2 script error · 3 docker unreachable/pull failure
            4 engine or verification failure (old store untouched) · 5 filesystem/swap failure (restored)
""";

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine(Usage);
    return args.Length == 0 ? (int)ExitCode.GuardRefusal : (int)ExitCode.Ok;
}

RewriteOptions options;
try
{
    options = ParseArgs(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine();
    Console.Error.WriteLine(Usage);
    return (int)ExitCode.GuardRefusal;
}

using var loggerFactory = LoggerFactory.Create(b => b
    .SetMinimumLevel(LogLevel.Information)
    .AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; }));

using var docker = new DockerClientConfiguration().CreateClient();
var report = await RewriteCommand.RunAsync(options, docker, loggerFactory);
Console.WriteLine(report.Format());
return (int)report.Code;

static RewriteOptions ParseArgs(string[] args)
{
    var container = args[0];
    if (container.StartsWith('-'))
        throw new ArgumentException("The first argument must be the container id or name.");

    string? script = null, eval = null, backupDir = null;
    // Credentials: flag beats environment beats the fleet default.
    var user = Environment.GetEnvironmentVariable("MP_REWRITE_USER") ?? DockerStore.DefaultUser;
    var password = Environment.GetEnvironmentVariable("MP_REWRITE_PASSWORD") ?? DockerStore.DefaultPassword;
    bool dryRun = false, noProjectionCopy = false, yes = false, forceVolume = false;
    var mode = RewriteMode.Rewrite;

    for (var i = 1; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--script": script = Next(args, ref i, "--script"); break;
            case "--eval": eval = Next(args, ref i, "--eval"); break;
            case "--user": user = Next(args, ref i, "--user"); break;
            case "--password": password = Next(args, ref i, "--password"); break;
            case "--dry-run": dryRun = true; break;
            case "--no-projection-copy": noProjectionCopy = true; break;
            case "--yes" or "-y": yes = true; break;
            // Still accepted so it fails with its REASON rather than "unknown argument"; RewriteCommand
            // refuses it. Deliberately absent from the usage above — it is reserved, not offered.
            case "--force-volume-copy": forceVolume = true; break;
            case "--status": mode = RewriteMode.Status; break;
            case "--rollback":
                mode = RewriteMode.Rollback;
                // The backup directory is optional; only consume the next token when it is not a flag.
                if (i + 1 < args.Length && !args[i + 1].StartsWith('-')) backupDir = args[++i];
                break;
            default: throw new ArgumentException($"Unknown argument: {args[i]}");
        }
    }

    // Deliberately NOT checked here: --script with --eval is a script-input error, and RewriteOptions
    // already rejects it. Deciding it in two places is how one invalid command line ends up with two
    // different exit codes depending on which check happens to run first.
    return new RewriteOptions
    {
        Container = container,
        ScriptPath = script,
        Eval = eval,
        DryRun = dryRun,
        NoProjectionCopy = noProjectionCopy,
        Yes = yes,
        ForceVolumeCopy = forceVolume,
        Mode = mode,
        BackupDir = backupDir,
        User = user,
        Password = password,
        // The fault injection is a TEST hook and is deliberately reachable only through an environment
        // variable, never a command-line flag: nothing an operator can mistype should be able to arm it.
        FailAfterSwapForTest = Environment.GetEnvironmentVariable("MP_REWRITE_TEST_FAIL_AFTER_SWAP") == "1"
    };
}

static string Next(string[] args, ref int i, string flag)
{
    if (i + 1 >= args.Length) throw new ArgumentException($"{flag} needs a value.");
    return args[++i];
}
