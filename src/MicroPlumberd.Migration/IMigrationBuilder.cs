using System.Text.Json.Nodes;

namespace MicroPlumberd.Migration;

/// <summary>
/// Fluent surface a <see cref="Migration"/> uses to declare RAW rewrite rules. All operations work on
/// event-type strings and JSON — never on domain event classes.
/// </summary>
/// <remarks>
/// <para><b>Ordering.</b> Operations are applied to every copied event in the exact order they are
/// declared, and migrations are applied in ascending <see cref="Migration.Id"/> order. Each operation
/// observes the running result of the operations before it: a <see cref="RenameType"/> declared before a
/// <see cref="TransformJson"/> that names the new type will compose, and a <see cref="RenameStream"/>
/// declared before a <see cref="DropStream(string)"/> that names the new stream will compose.</para>
/// <para><b>Short-circuit.</b> Once any drop operation drops an event, later operations for that event are
/// skipped.</para>
/// </remarks>
public interface IMigrationBuilder
{
    /// <summary>Drops (omits from the destination) every event whose current stream equals <paramref name="name"/>.</summary>
    IMigrationBuilder DropStream(string name);

    /// <summary>Drops every event whose current stream matches <paramref name="predicate"/>.</summary>
    IMigrationBuilder DropStream(Func<string, bool> predicate);

    /// <summary>Drops every event belonging to any of the given streams.</summary>
    IMigrationBuilder DropStreams(params string[] names);

    /// <summary>Drops individual events matching <paramref name="predicate"/> (the raw view reflects prior operations).</summary>
    IMigrationBuilder DropEvent(Func<RawEvent, bool> predicate);

    /// <summary>Rewrites the event-type string from <paramref name="oldType"/> to <paramref name="newType"/>.</summary>
    IMigrationBuilder RenameType(string oldType, string newType);

    /// <summary>
    /// Mutates the JSON payload and metadata in place for every event whose current type equals
    /// <paramref name="type"/>. Skipped (with a warning) for events whose payload is not JSON.
    /// </summary>
    IMigrationBuilder TransformJson(string type, Action<JsonNode, JsonNode> transform);

    /// <summary>Retargets every event from stream <paramref name="oldName"/> to stream <paramref name="newName"/>.</summary>
    IMigrationBuilder RenameStream(string oldName, string newName);

    /// <summary>
    /// Registers ONE generic per-event rule: every surviving (non-system, non-link) event is handed to
    /// <paramref name="transform"/> as an immutable <see cref="RawEvent"/>, and the returned event replaces it.
    /// Returning <c>null</c> — or an event with an empty <see cref="RawEvent.StreamId"/> or
    /// <see cref="RawEvent.Type"/> — DROPS the event.
    /// </summary>
    /// <remarks>
    /// <para>This is the escape hatch the type-specific operations above are sugar for, and what a
    /// script-defined migration compiles to. Like every other operation it is applied in DECLARATION ORDER, so
    /// a <see cref="DropStream(string)"/> declared before it short-circuits and the transform never sees those
    /// events, while a <see cref="RenameType"/> declared before it is already reflected in
    /// <see cref="RawEvent.Type"/>.</para>
    /// <para><b>Return the SAME <see cref="RawEvent.Data"/>/<see cref="RawEvent.Metadata"/> node instances when
    /// you did not change them</b> (e.g. return the argument itself). Change is detected by reference: an
    /// untouched payload is then copied BYTE-VERBATIM instead of being re-serialised, which is what keeps a pure
    /// copy identical to the source.</para>
    /// <para><see cref="RawEvent.EventNumber"/>, <see cref="RawEvent.EventId"/> and
    /// <see cref="RawEvent.Created"/> on the returned event are IGNORED: destination streams are renumbered
    /// gaplessly and the source event id and timestamp are preserved.</para>
    /// </remarks>
    IMigrationBuilder Transform(Func<RawEvent, RawEvent?> transform);
}
