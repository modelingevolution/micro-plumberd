using System.Text.Json;
using System.Text.Json.Serialization;

namespace MicroPlumberd.Services.BatchOperations;

/// <summary>
/// JSON converter for AppInstance that serializes to/from string format.
/// </summary>
public class AppInstanceJsonConverter : JsonConverter<AppInstance>
{
    /// <inheritdoc />
    public override AppInstance Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (string.IsNullOrEmpty(value))
        {
            return default;
        }

        if (AppInstance.TryParse(value, null, out var result))
        {
            return result;
        }

        throw new JsonException($"Could not parse '{value}' as AppInstance");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, AppInstance value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
