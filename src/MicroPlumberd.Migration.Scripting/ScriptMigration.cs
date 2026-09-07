using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace MicroPlumberd.Migration.Scripting;

/// <summary>
/// A <see cref="Migration"/> whose rules come from a JavaScript file rather than from C# code.
/// </summary>
/// <remarks>
/// <para>The script is PARSED IN THE CONSTRUCTOR. That is deliberate: a rewrite tool must reject a typo
/// before it starts a container, stops a store or renames a directory, so construction is the last cheap
/// moment to fail (<see cref="ScriptSyntaxException"/> carries the line and column).</para>
/// <para><see cref="Id"/> is <c>{idPrefix}_{sha256(source)[..12]}</c> — the prefix says WHEN/WHY the rewrite
/// ran, the hash says WHICH script did it, and the two together make each run its own history record.</para>
/// </remarks>
public sealed class ScriptMigration : Migration
{
    private readonly JsRuleHost _host;

    /// <summary>Loads and parses <paramref name="source"/>.</summary>
    /// <param name="source">The script text (the Replicator contract plus this library's helpers).</param>
    /// <param name="idPrefix">
    /// Stable, sortable prefix for <see cref="Id"/> — e.g. <c>rewrite_20260907T151200</c>. Two runs of the
    /// SAME script must not share an id unless they are meant to be the same migration: the history guard
    /// skips an id it has already applied.
    /// </param>
    /// <param name="logger">Sink for the script's <c>log.*</c> calls.</param>
    /// <exception cref="ScriptSyntaxException">The script does not parse.</exception>
    public ScriptMigration(string source, string idPrefix, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(idPrefix);

        Source = source;
        ScriptChecksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
        Id = $"{idPrefix}_{ScriptChecksum[..12]}";
        _host = new JsRuleHost(source, logger);
    }

    /// <summary>The script text this migration was built from.</summary>
    public string Source { get; }

    /// <summary>Uppercase hex SHA-256 of the script text — the identity of these rules.</summary>
    public string ScriptChecksum { get; }

    /// <inheritdoc />
    public override string Id { get; }

    /// <summary>
    /// The script's own hash, NOT the operation-descriptor checksum: every script compiles to the same host
    /// delegates, so descriptors cannot tell two scripts apart. See <see cref="Migration.ChecksumOverride"/>.
    /// </summary>
    public override string? ChecksumOverride => ScriptChecksum;

    /// <summary>True when the script declares no rule at all — the run is a pure copy.</summary>
    public bool IsPureCopy => _host.IsPureCopy;

    /// <inheritdoc />
    public override void Migrate(IMigrationBuilder b)
    {
        ArgumentNullException.ThrowIfNull(b);
        _host.Register(b);
    }
}
