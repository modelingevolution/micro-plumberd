using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace MicroPlumberd.Migration;

/// <summary>The effect a single operation had on one event.</summary>
internal enum OpEffect
{
    None,
    Dropped,
    Renamed,
    Transformed
}

/// <summary>
/// Mutable running state of one event as it is folded through the ordered operation list.
/// </summary>
internal sealed class EventContext
{
    // All settable so the copy loop can REUSE one instance per event (reset each iteration) — it never escapes
    // except into the synchronous DropEvent predicate via the immutable AsRawEvent() snapshot.
    public required string SourceStream { get; set; }
    public required ulong EventNumber { get; set; }
    public required string TargetStream { get; set; }
    public required string Type { get; set; }
    public JsonNode? Data { get; set; }
    public JsonNode? Meta { get; set; }
    public bool Dropped { get; set; }

    /// <summary>
    /// True once a <c>TransformJson</c> rule mutated this event's payload/metadata — the copy engine then
    /// re-serialises <see cref="Data"/>/<see cref="Meta"/>. When false the original bytes are copied VERBATIM
    /// (no parse, no re-serialise), even if the stream/type was renamed (those don't touch the payload).
    /// </summary>
    public bool Transformed { get; set; }

    /// <summary>Builds an immutable <see cref="RawEvent"/> view reflecting the current running state.</summary>
    public RawEvent AsRawEvent() => new()
    {
        StreamId = TargetStream,
        EventNumber = EventNumber,
        Type = Type,
        Data = Data,
        Metadata = Meta
    };
}

/// <summary>A single compiled migration operation.</summary>
internal interface IMigrationOp
{
    /// <summary>Deterministic textual description used for checksumming a migration's rules.</summary>
    string Descriptor { get; }

    /// <summary>Applies the operation to <paramref name="ctx"/>, returning the effect it had.</summary>
    OpEffect Apply(EventContext ctx, IMigrationOpLog? log);

    /// <summary>Static stream names this op references (for reserved-name validation). Empty when dynamic.</summary>
    IEnumerable<string> ReferencedStreamNames { get; }
}

/// <summary>Optional sink for per-op warnings during evaluation (e.g. transform skipped on non-JSON).</summary>
internal interface IMigrationOpLog
{
    void TransformSkippedNonJson(string type, string stream, ulong eventNumber);
}

internal static class DelegateChecksum
{
    /// <summary>
    /// Produces a checksum fragment for a delegate that is sensitive to BOTH which method backs it and
    /// the method's IL body — so editing a lambda's body changes the checksum. Falls back to the method
    /// identity when IL is unavailable (e.g. runtime-generated methods).
    /// </summary>
    public static string Describe(Delegate d)
    {
        var m = d.Method;
        var sb = new StringBuilder();
        sb.Append(m.DeclaringType?.FullName).Append('.').Append(m.Name);
        try
        {
            var il = m.GetMethodBody()?.GetILAsByteArray();
            if (il is { Length: > 0 })
            {
                var hash = SHA256.HashData(il);
                sb.Append(":il=").Append(Convert.ToHexString(hash));
            }
        }
        catch
        {
            // IL not accessible — method identity above is the best we can do.
        }
        return sb.ToString();
    }
}

internal sealed class DropStreamNameOp(string name) : IMigrationOp
{
    public string Descriptor => $"DropStream(\"{name}\")";
    public IEnumerable<string> ReferencedStreamNames => [name];

    public OpEffect Apply(EventContext ctx, IMigrationOpLog? log)
    {
        if (ctx.Dropped || ctx.TargetStream != name) return OpEffect.None;
        ctx.Dropped = true;
        return OpEffect.Dropped;
    }
}

internal sealed class DropStreamsOp(string[] names) : IMigrationOp
{
    private readonly HashSet<string> _names = new(names, StringComparer.Ordinal);
    public string Descriptor => $"DropStreams({string.Join(",", names.Select(n => $"\"{n}\""))})";
    public IEnumerable<string> ReferencedStreamNames => names;

    public OpEffect Apply(EventContext ctx, IMigrationOpLog? log)
    {
        if (ctx.Dropped || !_names.Contains(ctx.TargetStream)) return OpEffect.None;
        ctx.Dropped = true;
        return OpEffect.Dropped;
    }
}

internal sealed class DropStreamPredicateOp(Func<string, bool> predicate) : IMigrationOp
{
    public string Descriptor => $"DropStreamPredicate({DelegateChecksum.Describe(predicate)})";
    public IEnumerable<string> ReferencedStreamNames => [];

    public OpEffect Apply(EventContext ctx, IMigrationOpLog? log)
    {
        if (ctx.Dropped || !predicate(ctx.TargetStream)) return OpEffect.None;
        ctx.Dropped = true;
        return OpEffect.Dropped;
    }
}

internal sealed class DropEventOp(Func<RawEvent, bool> predicate) : IMigrationOp
{
    public string Descriptor => $"DropEvent({DelegateChecksum.Describe(predicate)})";
    public IEnumerable<string> ReferencedStreamNames => [];

    public OpEffect Apply(EventContext ctx, IMigrationOpLog? log)
    {
        if (ctx.Dropped || !predicate(ctx.AsRawEvent())) return OpEffect.None;
        ctx.Dropped = true;
        return OpEffect.Dropped;
    }
}

internal sealed class RenameTypeOp(string oldType, string newType) : IMigrationOp
{
    /// <summary>The new event-type name this rule assigns (validated against $-space by the runner).</summary>
    public string NewType => newType;

    public string Descriptor => $"RenameType(\"{oldType}\"->\"{newType}\")";
    public IEnumerable<string> ReferencedStreamNames => [];

    public OpEffect Apply(EventContext ctx, IMigrationOpLog? log)
    {
        if (ctx.Dropped || ctx.Type != oldType) return OpEffect.None;
        ctx.Type = newType;
        return OpEffect.Renamed;
    }
}

internal sealed class TransformJsonOp(string type, Action<JsonNode, JsonNode> transform) : IMigrationOp
{
    /// <summary>The event type this transform targets (used by the plan to decide which types need parsing).</summary>
    public string Type => type;

    public string Descriptor => $"TransformJson(\"{type}\",{DelegateChecksum.Describe(transform)})";
    public IEnumerable<string> ReferencedStreamNames => [];

    public OpEffect Apply(EventContext ctx, IMigrationOpLog? log)
    {
        if (ctx.Dropped || ctx.Type != type) return OpEffect.None;
        if (ctx.Data is null)
        {
            // Non-JSON payload: cannot hand the caller a JsonNode. Skip rather than fabricate data.
            log?.TransformSkippedNonJson(type, ctx.TargetStream, ctx.EventNumber);
            return OpEffect.None;
        }
        ctx.Meta ??= new JsonObject();
        transform(ctx.Data, ctx.Meta);
        ctx.Transformed = true; // payload/metadata mutated → the copy engine must re-serialise, not copy verbatim
        return OpEffect.Transformed;
    }
}

internal sealed class RenameStreamOp(string oldName, string newName) : IMigrationOp
{
    /// <summary>The new stream name this rule retargets events to (validated against $-space/reserved by the runner).</summary>
    public string NewName => newName;

    public string Descriptor => $"RenameStream(\"{oldName}\"->\"{newName}\")";
    public IEnumerable<string> ReferencedStreamNames => [oldName, newName];

    public OpEffect Apply(EventContext ctx, IMigrationOpLog? log)
    {
        if (ctx.Dropped || ctx.TargetStream != oldName) return OpEffect.None;
        ctx.TargetStream = newName;
        return OpEffect.Renamed;
    }
}
