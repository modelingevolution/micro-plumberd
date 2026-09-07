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

    /// <summary>The SOURCE event id (never rewritten — the copy engine preserves it on the destination).</summary>
    public Guid EventId { get; set; }

    /// <summary>The SOURCE write timestamp (UTC), exposed to rules so they can match by date.</summary>
    public DateTime Created { get; set; }

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
        Metadata = Meta,
        EventId = EventId,
        Created = Created
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

/// <summary>
/// The one GENERIC per-event operation: hands each surviving event to <paramref name="transform"/> as an
/// immutable <see cref="RawEvent"/> and folds the returned event back into the running state. Returning
/// <c>null</c> DROPS the event.
/// </summary>
/// <remarks>
/// <para>Unlike <see cref="TransformJsonOp"/> (which targets ONE event type and mutates the payload in place),
/// this op sees EVERY non-system, non-link event and may change the target stream, the event type, the payload
/// and the metadata in one pass. It is what a script-defined migration compiles to.</para>
/// <para><b>Change detection is by REFERENCE.</b> The returned <see cref="RawEvent.Data"/>/<see cref="RawEvent.Metadata"/>
/// are compared to the ones handed in with <c>ReferenceEquals</c>: a transform that did not touch the
/// payload returns the SAME node, the event is therefore NOT marked transformed, and the copy engine writes the
/// ORIGINAL BYTES verbatim. That is what keeps a pure copy byte-identical — re-serialising an untouched payload
/// would silently re-render its JSON (number formatting, key escapes) for every event in the store.</para>
/// <para><b><see cref="RawEvent.EventNumber"/>, <see cref="RawEvent.EventId"/> and <see cref="RawEvent.Created"/>
/// on the returned event are IGNORED</b> — the copy engine renumbers each destination stream gaplessly and
/// preserves the source event id and write timestamp.</para>
/// </remarks>
internal sealed class TransformOp(Func<RawEvent, RawEvent?> transform) : IMigrationOp
{
    public string Descriptor => $"Transform({DelegateChecksum.Describe(transform)})";
    public IEnumerable<string> ReferencedStreamNames => [];

    public OpEffect Apply(EventContext ctx, IMigrationOpLog? log)
    {
        if (ctx.Dropped) return OpEffect.None;

        var result = transform(ctx.AsRawEvent());
        if (result is null)
        {
            ctx.Dropped = true;
            return OpEffect.Dropped;
        }

        // An empty stream or event type is the script contract's second way of saying "drop" (Replicator).
        if (result.StreamId.Length == 0 || result.Type.Length == 0)
        {
            ctx.Dropped = true;
            return OpEffect.Dropped;
        }

        var renamed = false;
        if (!string.Equals(result.StreamId, ctx.TargetStream, StringComparison.Ordinal))
        {
            ctx.TargetStream = result.StreamId;
            renamed = true;
        }
        if (!string.Equals(result.Type, ctx.Type, StringComparison.Ordinal))
        {
            ctx.Type = result.Type;
            renamed = true;
        }

        var transformed = false;
        if (!ReferenceEquals(result.Data, ctx.Data))
        {
            ctx.Data = result.Data;
            transformed = true;
        }
        if (!ReferenceEquals(result.Metadata, ctx.Meta))
        {
            ctx.Meta = result.Metadata;
            transformed = true;
        }

        if (transformed)
        {
            ctx.Transformed = true; // payload/metadata changed → the copy engine must re-serialise
            return OpEffect.Transformed;
        }
        return renamed ? OpEffect.Renamed : OpEffect.None;
    }
}
