using System.Text.Json.Nodes;
using Acornima;
using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;
using Jint.Runtime.Descriptors;
using Jint.Runtime.Interop;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MicroPlumberd.Migration.Scripting;

/// <summary>
/// Loads a rewrite script into a sandboxed Jint engine and compiles it into <see cref="IMigrationBuilder"/>
/// operations.
/// </summary>
/// <remarks>
/// <para><b>The contract</b> is Kurrent Replicator's, so a script written for either tool runs on both: the
/// file may define <c>function transform(original)</c>, receiving
/// <c>{Stream, EventType, Data, Metadata, EventId, EventNumber, Created}</c> and returning the same shape, or
/// <c>undefined</c> / an empty <c>Stream</c> / an empty <c>EventType</c> to DROP the event. On top of that the
/// helpers <c>dropStream</c>, <c>dropEvent</c>, <c>update</c>, <c>updateById</c>, <c>renameType</c>,
/// <c>renameStream</c> and <c>log.*</c> keep the common repairs to one line each.</para>
/// <para><b>Ordering.</b> <c>dropStream</c>, <c>renameStream</c> and <c>renameType</c> are declarative: they
/// are collected while the script is loaded and registered as native builder operations, in that order,
/// BEFORE the single generic <c>Transform</c> that carries everything else. So a dropped stream never reaches
/// <c>update</c> or <c>transform</c> at all, and within one event the order is: <c>dropEvent</c> predicates →
/// <c>update</c>/<c>updateById</c> handlers → <c>transform</c>, which therefore sees the survivors, already
/// updated.</para>
/// <para><b>Sandbox.</b> CLR interop is left OFF (Jint's default): the script cannot reach a .NET type,
/// the filesystem or the network. Recursion is capped, and every event carries one wall-clock budget
/// (<see cref="PerEventBudgetConstraint"/>).</para>
/// <para><b>Threading.</b> A Jint engine is single-threaded and so is this host — which matches the copy
/// engine's single-threaded event loop. Do not share one instance across concurrent runs.</para>
/// </remarks>
internal sealed class JsRuleHost
{
    /// <summary>Wall-clock budget for ALL script code run for one event.</summary>
    private static readonly TimeSpan PerEventBudget = TimeSpan.FromSeconds(2);

    /// <summary>Max nested JS calls — a runaway recursive helper fails instead of taking the process down.</summary>
    private const int MaxRecursion = 64;

    private readonly Engine _engine;
    private readonly PerEventBudgetConstraint _budget = new(PerEventBudget);
    private readonly ILogger _logger;

    private readonly List<Func<string, bool>> _streamDrops = [];
    private readonly List<(string Old, string New)> _streamRenames = [];
    private readonly List<(string Old, string New)> _typeRenames = [];
    private readonly List<JsValue> _dropEvents = [];
    private readonly List<(string Type, JsValue Fn)> _updatesByType = [];
    private readonly List<(string Id, JsValue Fn)> _updatesById = [];
    private JsValue? _transform;

    private readonly JsValue _jsonParse;
    private readonly JsValue _jsonStringify;

    /// <summary>True once the script has been loaded; helper registration is closed after that.</summary>
    private bool _loaded;

    public JsRuleHost(string source, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        _logger = logger ?? NullLogger.Instance;

        _engine = new Engine(o => o
            .LimitRecursion(MaxRecursion)
            .Constraint(_budget));
        // NOTE: no AllowClr(), no modules, no host functions beyond the helpers below — the script's whole
        // world is ECMAScript plus what Register() puts in front of it.

        var json = _engine.GetValue("JSON");
        _jsonParse = json.Get("parse");
        _jsonStringify = json.Get("stringify");

        RegisterHelpers();

        try
        {
            _engine.Execute(source);
        }
        catch (ParseErrorException ex)
        {
            throw new ScriptSyntaxException(ex.LineNumber, ex.Column, ex.Message);
        }
        catch (JavaScriptException ex)
        {
            // A script that throws at LOAD time (e.g. a bad dropStream argument) is still an authoring error
            // the operator must see before anything is started.
            var loc = ex.Location;
            throw new ScriptSyntaxException(loc.Start.Line, loc.Start.Column + 1, ex.Message);
        }

        var t = _engine.GetValue("transform");
        _transform = t.IsObject() && t.IsCallable() ? t : null;
        _loaded = true;
    }

    /// <summary>True when the script declared no rule at all — a pure copy.</summary>
    public bool IsPureCopy =>
        _streamDrops.Count == 0 && _streamRenames.Count == 0 && _typeRenames.Count == 0 &&
        _dropEvents.Count == 0 && _updatesByType.Count == 0 && _updatesById.Count == 0 && _transform is null;

    /// <summary>Registers the script's rules on <paramref name="b"/>, in the order documented on the class.</summary>
    public void Register(IMigrationBuilder b)
    {
        foreach (var drop in _streamDrops) b.DropStream(drop);
        foreach (var (o, n) in _streamRenames) b.RenameStream(o, n);
        foreach (var (o, n) in _typeRenames) b.RenameType(o, n);

        var needsPerEvent = _dropEvents.Count > 0 || _updatesByType.Count > 0 || _updatesById.Count > 0
                            || _transform is not null;
        if (needsPerEvent) b.Transform(Apply);
    }

    // ---------------------------------------------------------------- helper registration

    private void RegisterHelpers()
    {
        Define("dropStream", (_, args) =>
        {
            RequireLoading("dropStream");
            if (args.Length == 0) throw Throw("dropStream(name | RegExp | fn) requires one argument.");
            _streamDrops.Add(StreamPredicate(args[0]));
            return JsValue.Undefined;
        });

        Define("dropEvent", (_, args) =>
        {
            RequireLoading("dropEvent");
            _dropEvents.Add(RequireFunction(args, 0, "dropEvent(fn)"));
            return JsValue.Undefined;
        });

        Define("update", (_, args) =>
        {
            RequireLoading("update");
            _updatesByType.Add((RequireString(args, 0, "update(type, fn)"), RequireFunction(args, 1, "update(type, fn)")));
            return JsValue.Undefined;
        });

        Define("updateById", (_, args) =>
        {
            RequireLoading("updateById");
            _updatesById.Add((RequireString(args, 0, "updateById(id, fn)"), RequireFunction(args, 1, "updateById(id, fn)")));
            return JsValue.Undefined;
        });

        Define("renameType", (_, args) =>
        {
            RequireLoading("renameType");
            _typeRenames.Add((RequireString(args, 0, "renameType(from, to)"), RequireString(args, 1, "renameType(from, to)")));
            return JsValue.Undefined;
        });

        Define("renameStream", (_, args) =>
        {
            RequireLoading("renameStream");
            _streamRenames.Add((RequireString(args, 0, "renameStream(from, to)"), RequireString(args, 1, "renameStream(from, to)")));
            return JsValue.Undefined;
        });

        _engine.SetValue("log", ScriptLog.Create(_engine, _logger));
    }

    private void Define(string name, Func<JsValue, JsValue[], JsValue> fn) =>
        _engine.SetValue(name, new ClrFunction(_engine, name, fn));

    /// <summary>
    /// The declarative helpers describe the RULE SET, which is fixed once the file has been loaded. Calling one
    /// from inside <c>transform</c> would silently do nothing (the plan is already compiled), so it fails loudly.
    /// </summary>
    private void RequireLoading(string name)
    {
        if (_loaded)
            throw Throw($"{name}() may only be called at the top level of the script, not while events are "
                        + "being processed — the rule set is fixed once the script has been loaded.");
    }

    private Func<string, bool> StreamPredicate(JsValue arg)
    {
        if (arg.IsString())
        {
            var name = arg.AsString();
            return s => string.Equals(s, name, StringComparison.Ordinal);
        }
        if (arg.IsRegExp())
        {
            var re = arg.AsObject();
            var test = re.Get("test");
            return s =>
            {
                // A /g/ or /y/ regexp carries lastIndex across calls, so the SAME pattern would match every
                // other stream. Reset it: a rule must not depend on how many streams preceded this one.
                re.Set("lastIndex", 0);
                return TypeConverter.ToBoolean(_engine.Invoke(test, re, [JsString.Create(s)]));
            };
        }
        if (arg.IsCallable())
        {
            var fn = arg;
            return s => TypeConverter.ToBoolean(_engine.Invoke(fn, JsValue.Undefined, [JsString.Create(s)]));
        }
        throw Throw("dropStream() accepts a stream name, a RegExp, or a function of the stream name.");
    }

    private JsValue RequireFunction(JsValue[] args, int i, string usage)
    {
        if (args.Length <= i || !args[i].IsCallable()) throw Throw($"{usage}: argument {i + 1} must be a function.");
        return args[i];
    }

    private string RequireString(JsValue[] args, int i, string usage)
    {
        if (args.Length <= i || !args[i].IsString()) throw Throw($"{usage}: argument {i + 1} must be a string.");
        var s = args[i].AsString();
        if (s.Length == 0) throw Throw($"{usage}: argument {i + 1} must not be empty.");
        return s;
    }

    private JavaScriptException Throw(string message) =>
        new(_engine.Intrinsics.Error, message);

    // ---------------------------------------------------------------- per-event evaluation

    /// <summary>
    /// The single generic rule the script compiles to. Returns <c>null</c> to drop, the SAME
    /// <see cref="RawEvent"/> instance when nothing changed (so the copy engine writes the original bytes
    /// verbatim), or a rewritten one.
    /// </summary>
    private RawEvent? Apply(RawEvent e)
    {
        // Only JSON payloads are transformable (requirements). A binary / unparseable payload bypasses the
        // script entirely and is copied verbatim — never dropped by a rule that could not even see it.
        if (e.Data is null) return e;

        _budget.BeginEvent();
        try
        {
            var current = ToJsEvent(e);
            // Snapshot the payload AS JAVASCRIPT SEES IT, before any rule runs. Comparing the script's result
            // against this — rather than against the .NET node's own text — is what makes "the script changed
            // nothing" decidable: the trip through JS canonicalises numbers (1.0 renders as 1), so a .NET-side
            // comparison reports every such payload as changed and re-serialises a store that nobody edited.
            var dataIn = Snapshot(current.Get("Data"));
            var metaIn = Snapshot(current.Get("Metadata"));

            foreach (var pred in _dropEvents)
                if (TypeConverter.ToBoolean(_engine.Invoke(pred, JsValue.Undefined, [current])))
                    return null;

            foreach (var (type, fn) in _updatesByType)
            {
                if (!string.Equals(current.Get("EventType").ToString(), type, StringComparison.Ordinal)) continue;
                var next = _engine.Invoke(fn, JsValue.Undefined, [current]);
                if (IsDrop(next)) return null;
                current = RequireEventObject(next, e, "update");
            }

            foreach (var (id, fn) in _updatesById)
            {
                if (!string.Equals(e.EventId.ToString(), id, StringComparison.OrdinalIgnoreCase)) continue;
                var next = _engine.Invoke(fn, JsValue.Undefined, [current]);
                if (IsDrop(next)) return null;
                current = RequireEventObject(next, e, "updateById");
            }

            if (_transform is not null)
            {
                var next = _engine.Invoke(_transform, JsValue.Undefined, [current]);
                if (IsDrop(next)) return null;
                current = RequireEventObject(next, e, "transform");
            }

            return FromJsEvent(current, e, dataIn, metaIn);
        }
        catch (ScriptExecutionException)
        {
            throw;
        }
        catch (TimeoutException ex)
        {
            throw new ScriptExecutionException(e.StreamId, e.EventNumber,
                $"{ex.Message} An event-level budget is the only defence against a script that never returns.", ex);
        }
        catch (JavaScriptException ex)
        {
            throw new ScriptExecutionException(e.StreamId, e.EventNumber, ex.Message, ex);
        }
        catch (JintException ex)
        {
            throw new ScriptExecutionException(e.StreamId, e.EventNumber, ex.Message, ex);
        }
        finally
        {
            _budget.EndEvent();
        }
    }

    private static bool IsDrop(JsValue v) => v.IsUndefined() || v.IsNull();

    private static ObjectInstance RequireEventObject(JsValue v, RawEvent e, string what)
    {
        if (!v.IsCallable() && v is ObjectInstance o) return o;
        throw new ScriptExecutionException(e.StreamId, e.EventNumber,
            $"{what}() must return an event object (or undefined to drop) — it returned {Describe(v)}.");
    }

    private static string Describe(JsValue v) =>
        v.IsString() ? $"the string \"{v.AsString()}\"" :
        v.IsNumber() ? $"the number {v.AsNumber()}" :
        v.IsBoolean() ? $"the boolean {v.AsBoolean()}" :
        v.IsCallable() ? "a function" :
        v.Type.ToString().ToLowerInvariant();

    // ---------------------------------------------------------------- marshalling

    /// <summary>
    /// Builds the script's view of one event. <c>Data</c>/<c>Metadata</c> cross as real JS objects through the
    /// engine's own <c>JSON.parse</c> — never <c>JsValue.FromObject</c> on a <see cref="JsonNode"/>, which would
    /// hand the script a CLR wrapper whose members are .NET's, not JavaScript's.
    /// </summary>
    private ObjectInstance ToJsEvent(RawEvent e)
    {
        var o = new JsObject(_engine);
        o.FastSetDataProperty("Stream", e.StreamId);
        o.FastSetDataProperty("EventType", e.Type);
        o.FastSetDataProperty("Data", ToJs(e.Data));
        o.FastSetDataProperty("Metadata", ToJs(e.Metadata));
        // Identity, read-only: the copy preserves the source id, renumbers the destination stream, and stamps
        // its own write time — assigning to these could only mislead the script's author.
        o.FastSetProperty("EventId", ReadOnly(JsString.Create(e.EventId.ToString())));
        o.FastSetProperty("EventNumber", ReadOnly(JsNumber.Create((double)e.EventNumber)));
        // ISO-8601 so the documented `e.Created < "2026-08-25"` string comparison orders correctly.
        o.FastSetProperty("Created", ReadOnly(JsString.Create(
            e.Created.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture))));
        return o;
    }

    /// <summary>
    /// Reads <c>Stream</c> / <c>EventType</c> off the returned object, insisting it is a string.
    /// </summary>
    /// <remarks>
    /// Absent is NOT empty. Building a fresh result object and forgetting a field is the most likely mistake a
    /// script author makes, and the contract's "an empty <c>Stream</c> drops the event" would otherwise turn it
    /// into total, silent data loss at exit 0. A non-string is rejected for the same reason from the other
    /// side: coercing <c>123</c> to <c>"123"</c> writes events into a stream nobody named. Neither is a drop,
    /// so both are errors — and a script that means "drop" still writes <c>undefined</c> or <c>""</c>, which is
    /// what Replicator documents.
    /// </remarks>
    private static string RequiredString(ObjectInstance o, string property, RawEvent e)
    {
        var v = o.Get(property);
        if (v.IsUndefined() || v.IsNull())
            throw new ScriptExecutionException(e.StreamId, e.EventNumber,
                $"the returned event has no '{property}'. That is not the same as an empty '{property}', which "
                + "would DROP the event — return the original's value, or an explicit empty string if dropping "
                + "is what you meant.");
        if (!v.IsString())
            throw new ScriptExecutionException(e.StreamId, e.EventNumber,
                $"the returned event's '{property}' is {Describe(v)}, but it must be a string.");
        return v.AsString();
    }

    /// <summary>The JSON text of a value as JavaScript renders it, or <c>null</c> for undefined/null.</summary>
    private string? Snapshot(JsValue v)
    {
        if (v.IsUndefined() || v.IsNull()) return null;
        var text = _engine.Invoke(_jsonStringify, JsValue.Undefined, [v]);
        return text.IsString() ? text.AsString() : null;
    }

    private static PropertyDescriptor ReadOnly(JsValue v) =>
        new(v, writable: false, enumerable: true, configurable: false);

    private JsValue ToJs(JsonNode? node) =>
        node is null ? JsValue.Undefined : _engine.Invoke(_jsonParse, JsValue.Undefined, [JsString.Create(node.ToJsonString())]);

    /// <summary>
    /// Reads the script's result back. Returns the ORIGINAL <see cref="RawEvent"/> instance when nothing
    /// changed, so the copy engine takes its byte-verbatim path.
    /// </summary>
    private RawEvent? FromJsEvent(ObjectInstance o, RawEvent original, string? dataIn, string? metaIn)
    {
        var streamName = RequiredString(o, "Stream", original);
        var typeName = RequiredString(o, "EventType", original);

        // The Replicator contract's second way of saying "drop" — an EXPLICIT empty string. An absent or
        // non-string property is an authoring error and was rejected above; folding it in here is what would
        // turn one forgotten field into the silent deletion of every event the rule touched.
        if (streamName.Length == 0 || typeName.Length == 0) return null;

        var data = FromJs(o.Get("Data"), original.Data, dataIn, original, "Data");
        var meta = FromJs(o.Get("Metadata"), original.Metadata, metaIn, original, "Metadata");

        if (ReferenceEquals(data, original.Data) && ReferenceEquals(meta, original.Metadata)
            && string.Equals(streamName, original.StreamId, StringComparison.Ordinal)
            && string.Equals(typeName, original.Type, StringComparison.Ordinal))
            return original;

        return original with { StreamId = streamName, Type = typeName, Data = data, Metadata = meta };
    }

    /// <summary>
    /// Converts one JS value back to a <see cref="JsonNode"/> via the engine's <c>JSON.stringify</c>, returning
    /// <paramref name="original"/> UNCHANGED (same reference) when the round-trip is textually identical — which
    /// is what lets an untouched payload be copied byte-for-byte instead of re-rendered.
    /// </summary>
    private JsonNode? FromJs(JsValue v, JsonNode? original, string? snapshot, RawEvent e, string what)
    {
        if (v.IsUndefined() || v.IsNull())
        {
            // A script that CLEARS metadata means it: return an empty object rather than null, which the copy
            // engine would read as "unchanged" and silently write the original metadata back.
            if (original is null) return null;
            return what == "Metadata" ? new JsonObject() : throw new ScriptExecutionException(
                e.StreamId, e.EventNumber, "Data must not be removed — return undefined to drop the event.");
        }

        var text = _engine.Invoke(_jsonStringify, JsValue.Undefined, [v]);
        if (!text.IsString())
            throw new ScriptExecutionException(e.StreamId, e.EventNumber,
                $"{what} is not JSON-serialisable (JSON.stringify returned {Describe(text)}).");

        var json = text.AsString();
        if (original is not null && snapshot is not null && string.Equals(json, snapshot, StringComparison.Ordinal))
            return original;

        try
        {
            return JsonNode.Parse(json);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new ScriptExecutionException(e.StreamId, e.EventNumber,
                $"{what} could not be read back as JSON: {ex.Message}", ex);
        }
    }
}
