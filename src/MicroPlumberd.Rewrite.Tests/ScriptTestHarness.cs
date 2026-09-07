using System.Text.Json.Nodes;
using MicroPlumberd.Migration;
using MicroPlumberd.Migration.Scripting;

namespace MicroPlumberd.Rewrite.Tests;

/// <summary>
/// Records what a <see cref="Migration"/> declares, in declaration order, and hands back the generic
/// <c>Transform</c> rule so a test can run one event through the script without a store.
/// </summary>
internal sealed class RecordingBuilder : IMigrationBuilder
{
    /// <summary>Every operation the migration declared, in order, as <c>Name(args)</c>.</summary>
    public List<string> Calls { get; } = [];

    /// <summary>The stream predicates registered by <c>dropStream</c>, in order.</summary>
    public List<Func<string, bool>> StreamDrops { get; } = [];

    /// <summary>The single generic per-event rule, or <c>null</c> when the script declared none.</summary>
    public Func<RawEvent, RawEvent?>? Transformer { get; private set; }

    public IMigrationBuilder DropStream(string name)
    {
        Calls.Add($"DropStream(\"{name}\")");
        StreamDrops.Add(s => s == name);
        return this;
    }

    public IMigrationBuilder DropStream(Func<string, bool> predicate)
    {
        Calls.Add("DropStream(predicate)");
        StreamDrops.Add(predicate);
        return this;
    }

    public IMigrationBuilder DropStreams(params string[] names)
    {
        Calls.Add($"DropStreams({string.Join(",", names)})");
        return this;
    }

    public IMigrationBuilder DropEvent(Func<RawEvent, bool> predicate)
    {
        Calls.Add("DropEvent(predicate)");
        return this;
    }

    public IMigrationBuilder RenameType(string oldType, string newType)
    {
        Calls.Add($"RenameType(\"{oldType}\"->\"{newType}\")");
        return this;
    }

    public IMigrationBuilder TransformJson(string type, Action<JsonNode, JsonNode> transform)
    {
        Calls.Add($"TransformJson(\"{type}\")");
        return this;
    }

    public IMigrationBuilder RenameStream(string oldName, string newName)
    {
        Calls.Add($"RenameStream(\"{oldName}\"->\"{newName}\")");
        return this;
    }

    public IMigrationBuilder Transform(Func<RawEvent, RawEvent?> transform)
    {
        Calls.Add("Transform");
        Transformer = transform;
        return this;
    }
}

/// <summary>Compiles a script and runs single events through it, in-process, with no store involved.</summary>
internal sealed class ScriptRunner
{
    private readonly RecordingBuilder _builder = new();

    public ScriptRunner(string script, string idPrefix = "ut_20260907T000000")
    {
        Migration = new ScriptMigration(script, idPrefix);
        Migration.Migrate(_builder);
    }

    public ScriptMigration Migration { get; }
    public IReadOnlyList<string> Calls => _builder.Calls;
    public IReadOnlyList<Func<string, bool>> StreamDrops => _builder.StreamDrops;

    /// <summary>The generic per-event rule the script compiled to (fails the test if it declared none).</summary>
    public Func<RawEvent, RawEvent?> Transformer =>
        _builder.Transformer ?? throw new InvalidOperationException(
            "The script declared no per-event rule — there is nothing to run an event through.");

    public RawEvent? Run(RawEvent e) => Transformer(e);
}

/// <summary>Builds <see cref="RawEvent"/>s for the in-process script tests.</summary>
internal static class Ev
{
    public static readonly Guid KnownId = Guid.Parse("8d8066f9-bfbb-423e-a990-3fa5f7c6d4d6");

    public static RawEvent Make(string stream = "Order-1", string type = "OrderCreated",
        string data = "{\"n\":1}", string? meta = "{\"$correlationId\":\"c\"}",
        ulong number = 0, Guid? id = null, DateTime? created = null) => new()
    {
        StreamId = stream,
        EventNumber = number,
        Type = type,
        Data = data is null ? null : JsonNode.Parse(data),
        Metadata = meta is null ? null : JsonNode.Parse(meta),
        EventId = id ?? KnownId,
        Created = created ?? new DateTime(2026, 8, 20, 10, 30, 0, DateTimeKind.Utc)
    };

    /// <summary>A raw event whose payload is not JSON at all (the binary / corrupt case).</summary>
    public static RawEvent NonJson(string stream = "Blob-1", string type = "Blob") => new()
    {
        StreamId = stream,
        EventNumber = 0,
        Type = type,
        Data = null,
        Metadata = null,
        EventId = KnownId,
        Created = new DateTime(2026, 8, 20, 10, 30, 0, DateTimeKind.Utc)
    };
}
