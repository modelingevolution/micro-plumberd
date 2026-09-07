namespace MicroPlumberd.Migration.Scripting;

/// <summary>
/// The script could not be PARSED. Thrown by the <see cref="ScriptMigration"/> constructor, i.e. before any
/// store is touched, so a typo can never leave a half-rewritten system behind.
/// </summary>
public sealed class ScriptSyntaxException(int line, int column, string description)
    : Exception($"Script syntax error at line {line}, column {column}: {description}")
{
    /// <summary>1-based line of the offending token.</summary>
    public int Line { get; } = line;

    /// <summary>1-based column of the offending token.</summary>
    public int Column { get; } = column;

    /// <summary>The parser's own description of the problem.</summary>
    public string Description { get; } = description;
}

/// <summary>
/// A script rule THREW, ran past its per-event time budget, or returned something the contract does not
/// allow, while processing one specific event — which the message names, because "the script failed" is
/// useless to an operator holding a store with a million events.
/// </summary>
public sealed class ScriptExecutionException(string stream, ulong eventNumber, string reason, Exception? inner = null)
    : Exception($"Script failed on {stream}#{eventNumber}: {reason}", inner)
{
    /// <summary>The stream of the event being processed.</summary>
    public string Stream { get; } = stream;

    /// <summary>The source event number of the event being processed.</summary>
    public ulong EventNumber { get; } = eventNumber;
}
