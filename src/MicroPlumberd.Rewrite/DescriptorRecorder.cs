using System.Text.Json.Nodes;
using MicroPlumberd.Migration;

namespace MicroPlumberd.Rewrite;

/// <summary>
/// Captures the descriptors a migration compiles to, so the tool can print the rules in its plan and its
/// report BEFORE the operator commits to the run.
/// </summary>
/// <remarks>
/// The engine records the same descriptors in the store's history, but only after the run. An operator being
/// asked to confirm needs to see what the script was understood to mean while there is still time to say no —
/// "it dropped the wrong streams" is not a thing to learn from a history record.
/// </remarks>
internal sealed class DescriptorRecorder : IMigrationBuilder
{
    private readonly List<string> _descriptors = [];

    public IReadOnlyList<string> Descriptors => _descriptors;

    public IMigrationBuilder DropStream(string name) => Add($"DropStream(\"{name}\")");
    public IMigrationBuilder DropStream(Func<string, bool> predicate) => Add("DropStreamPredicate(script)");
    public IMigrationBuilder DropStreams(params string[] names) => Add($"DropStreams({string.Join(",", names)})");
    public IMigrationBuilder DropEvent(Func<RawEvent, bool> predicate) => Add("DropEvent(script)");
    public IMigrationBuilder RenameType(string oldType, string newType) => Add($"RenameType(\"{oldType}\"->\"{newType}\")");
    public IMigrationBuilder TransformJson(string type, Action<JsonNode, JsonNode> transform) => Add($"TransformJson(\"{type}\")");
    public IMigrationBuilder RenameStream(string oldName, string newName) => Add($"RenameStream(\"{oldName}\"->\"{newName}\")");
    public IMigrationBuilder Transform(Func<RawEvent, RawEvent?> transform) => Add("Transform(script)");

    private IMigrationBuilder Add(string descriptor)
    {
        _descriptors.Add(descriptor);
        return this;
    }
}
