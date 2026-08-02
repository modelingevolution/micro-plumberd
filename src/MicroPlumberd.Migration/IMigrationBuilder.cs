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
}
