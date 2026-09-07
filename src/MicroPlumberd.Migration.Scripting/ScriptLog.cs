using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime.Interop;
using Microsoft.Extensions.Logging;

namespace MicroPlumberd.Migration.Scripting;

/// <summary>
/// Builds the script-visible <c>log</c> object (<c>log.debug/info/warn/error</c>), bridging a
/// Serilog-style message template plus values onto <see cref="ILogger"/>.
/// </summary>
/// <remarks>
/// The object is assembled from Jint <see cref="ClrFunction"/>s rather than by handing Jint a CLR instance:
/// wrapping a CLR object would expose its whole reflection surface (<c>GetType()</c>, and from there the
/// loaded assemblies) to the script, which is exactly what the sandbox exists to prevent.
/// </remarks>
internal static class ScriptLog
{
    public static ObjectInstance Create(Engine engine, ILogger logger)
    {
        var log = new JsObject(engine);
        log.FastSetDataProperty("debug", Level(engine, logger, LogLevel.Debug, "debug"));
        log.FastSetDataProperty("info", Level(engine, logger, LogLevel.Information, "info"));
        log.FastSetDataProperty("warn", Level(engine, logger, LogLevel.Warning, "warn"));
        log.FastSetDataProperty("error", Level(engine, logger, LogLevel.Error, "error"));
        return log;
    }

    private static ClrFunction Level(Engine engine, ILogger logger, LogLevel level, string name) =>
        new(engine, name, (_, args) =>
        {
            if (args.Length == 0) return JsValue.Undefined;
            var template = args[0].IsString() ? args[0].AsString() : args[0].ToString();
            var values = new object?[args.Length - 1];
            for (var i = 1; i < args.Length; i++) values[i - 1] = ToClr(args[i]);
#pragma warning disable CA2254 // the template IS the script author's — that is the feature
            logger.Log(level, template, values);
#pragma warning restore CA2254
            return JsValue.Undefined;
        });

    // Only PRIMITIVES cross as themselves; anything structural is rendered as JSON-ish text via the engine's
    // own ToString. ToObject() on an object would hand the logger a Jint wrapper whose ToString is useless.
    private static object? ToClr(JsValue v) => v switch
    {
        _ when v.IsNull() || v.IsUndefined() => null,
        _ when v.IsString() => v.AsString(),
        _ when v.IsBoolean() => v.AsBoolean(),
        _ when v.IsNumber() => v.AsNumber(),
        _ => v.ToString()
    };
}
