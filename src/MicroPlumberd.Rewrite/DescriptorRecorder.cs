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
    private readonly List<Func<string, bool>> _streamDrops = [];
    private readonly List<(string From, string To)> _streamRenames = [];
    private bool _eventLevelDrop;

    public IReadOnlyList<string> Descriptors => _descriptors;

    /// <summary>
    /// What this rule set can explain about a whole stream disappearing — the real predicates, not their
    /// descriptions, because the verification gate has to ASK them about a specific stream name.
    /// </summary>
    public RuleReach Reach => new(_streamDrops, _streamRenames, _eventLevelDrop);

    public IMigrationBuilder DropStream(string name)
    {
        _streamDrops.Add(s => string.Equals(s, name, StringComparison.Ordinal));
        return Add($"DropStream(\"{name}\")");
    }

    public IMigrationBuilder DropStream(Func<string, bool> predicate)
    {
        _streamDrops.Add(predicate);
        return Add("DropStreamPredicate(script)");
    }

    public IMigrationBuilder DropStreams(params string[] names)
    {
        var set = new HashSet<string>(names, StringComparer.Ordinal);
        _streamDrops.Add(set.Contains);
        return Add($"DropStreams({string.Join(",", names)})");
    }

    // DropEvent and Transform can empty a stream one event at a time, so their presence makes ANY stream's
    // disappearance attributable. Neither can be asked "would you have emptied THIS stream?" without replaying
    // the whole copy, which is why the gate can only be decisive when no such rule exists — most importantly
    // for a pure copy, where nothing may vanish at all.
    public IMigrationBuilder DropEvent(Func<RawEvent, bool> predicate)
    {
        _eventLevelDrop = true;
        return Add("DropEvent(script)");
    }

    public IMigrationBuilder Transform(Func<RawEvent, RawEvent?> transform)
    {
        _eventLevelDrop = true;
        return Add("Transform(script)");
    }

    public IMigrationBuilder RenameStream(string oldName, string newName)
    {
        _streamRenames.Add((oldName, newName));
        return Add($"RenameStream(\"{oldName}\"->\"{newName}\")");
    }

    public IMigrationBuilder RenameType(string oldType, string newType) => Add($"RenameType(\"{oldType}\"->\"{newType}\")");
    public IMigrationBuilder TransformJson(string type, Action<JsonNode, JsonNode> transform) => Add($"TransformJson(\"{type}\")");

    private IMigrationBuilder Add(string descriptor)
    {
        _descriptors.Add(descriptor);
        return this;
    }
}
