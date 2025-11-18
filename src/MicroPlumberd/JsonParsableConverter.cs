using System.Text.Json;
using System.Text.Json.Serialization;

namespace MicroPlumberd;

/// <summary>
/// JSON converter for types that implement IParsable interface.
/// </summary>
/// <typeparam name="T">The type to convert, must implement IParsable.</typeparam>
public class JsonParsableConverter<T> : JsonConverter<T> where T : IParsable<T>
{
    /// <summary>
    /// Reads and converts JSON to the specified type.
    /// </summary>
    /// <param name="reader">The reader to read from.</param>
    /// <param name="typeToConvert">The type to convert to.</param>
    /// <param name="options">Serializer options.</param>
    /// <returns>The converted value.</returns>
    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.GetString();
        //Console.WriteLine($"Parsing: {str}");
        return T.Parse(str, null);
    }

    /// <summary>
    /// Writes the specified value as JSON.
    /// </summary>
    /// <param name="writer">The writer to write to.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="options">Serializer options.</param>
    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}