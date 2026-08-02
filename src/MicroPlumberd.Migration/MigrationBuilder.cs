using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace MicroPlumberd.Migration;

/// <summary>Records the ordered operations a single <see cref="Migration"/> declares.</summary>
internal sealed class MigrationBuilder : IMigrationBuilder
{
    private readonly List<IMigrationOp> _ops = new();
    public IReadOnlyList<IMigrationOp> Operations => _ops;

    public IMigrationBuilder DropStream(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        _ops.Add(new DropStreamNameOp(name));
        return this;
    }

    public IMigrationBuilder DropStream(Func<string, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        _ops.Add(new DropStreamPredicateOp(predicate));
        return this;
    }

    public IMigrationBuilder DropStreams(params string[] names)
    {
        ArgumentNullException.ThrowIfNull(names);
        _ops.Add(new DropStreamsOp(names));
        return this;
    }

    public IMigrationBuilder DropEvent(Func<RawEvent, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        _ops.Add(new DropEventOp(predicate));
        return this;
    }

    public IMigrationBuilder RenameType(string oldType, string newType)
    {
        ArgumentException.ThrowIfNullOrEmpty(oldType);
        ArgumentException.ThrowIfNullOrEmpty(newType);
        _ops.Add(new RenameTypeOp(oldType, newType));
        return this;
    }

    public IMigrationBuilder TransformJson(string type, Action<JsonNode, JsonNode> transform)
    {
        ArgumentException.ThrowIfNullOrEmpty(type);
        ArgumentNullException.ThrowIfNull(transform);
        _ops.Add(new TransformJsonOp(type, transform));
        return this;
    }

    public IMigrationBuilder RenameStream(string oldName, string newName)
    {
        ArgumentException.ThrowIfNullOrEmpty(oldName);
        ArgumentException.ThrowIfNullOrEmpty(newName);
        _ops.Add(new RenameStreamOp(oldName, newName));
        return this;
    }
}

/// <summary>
/// A <see cref="Migration"/> whose <see cref="Migration.Migrate"/> has been run to capture its ordered
/// operation list, together with a checksum over that list.
/// </summary>
internal sealed class CompiledMigration
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required IReadOnlyList<IMigrationOp> Operations { get; init; }
    public required string Checksum { get; init; }

    public static CompiledMigration Compile(Migration migration)
    {
        var b = new MigrationBuilder();
        migration.Migrate(b);
        return new CompiledMigration
        {
            Id = migration.Id,
            Name = migration.Name,
            Operations = b.Operations,
            Checksum = ComputeChecksum(migration.Id, b.Operations)
        };
    }

    /// <summary>
    /// SHA-256 over the migration Id and each operation's descriptor. The descriptor captures literal
    /// arguments and, for delegate-backed operations, the delegate's method identity and IL body hash —
    /// so editing an already-applied migration's rules changes the checksum and the run is refused.
    /// </summary>
    /// <remarks>
    /// Limitation: a pure lambda-body edit is only detected via IL when the method body is reflectable;
    /// literal/structural edits are always detected.
    /// </remarks>
    internal static string ComputeChecksum(string id, IReadOnlyList<IMigrationOp> ops)
    {
        var sb = new StringBuilder();
        sb.Append("id=").Append(id).Append('\n');
        for (var i = 0; i < ops.Count; i++)
            sb.Append(i).Append(':').Append(ops[i].Descriptor).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }
}
