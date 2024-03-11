using System.Collections;
using System.Dynamic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MicroPlumberd;

class ObjectSerializer : IObjectSerializer
{
    public static JsonSerializerOptions Options = new() { Converters = { new ExpandoObjectConverter() }};
    private static JsonElement Empty = JsonSerializer.Deserialize<JsonElement>("{}");
    public object? Deserialize(ReadOnlySpan<byte> span, Type t)
    {
        return JsonSerializer.Deserialize(span, t, Options);
    }

    public JsonElement Parse(ReadOnlySpan<byte> span)
    {
        if(span.Length == 0) return Empty;
        return JsonSerializer.Deserialize<JsonElement>(span, Options);
    }

    public byte[] SerializeToUtf8Bytes(object? t)
    {
        return t == null ? Array.Empty<byte>() : JsonSerializer.SerializeToUtf8Bytes(t, t.GetType(), Options);
    }
}
class ExpandoObjectConverter : JsonConverter<ExpandoObject>
{
    public override ExpandoObject Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return ReadValue(ref reader, options);
    }

    private ExpandoObject ReadValue(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException();
        }

        var expando = new ExpandoObject();
        var dictionary = (IDictionary<string, object>)expando;

        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.EndObject:
                    return expando;
                case JsonTokenType.PropertyName:
                    var propertyName = reader.GetString();
                    reader.Read();
                    dictionary[propertyName] = ReadObject(ref reader, options);
                    break;
                default:
                    throw new JsonException();
            }
        }

        throw new JsonException("Expected EndObject token not found.");
    }

    private object ReadObject(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.StartObject:
                return ReadValue(ref reader, options);
            case JsonTokenType.StartArray:
                return ReadArray(ref reader, options);
            case JsonTokenType.String:
                return reader.GetString();
            case JsonTokenType.Number:
                if (reader.TryGetInt32(out int intValue))
                {
                    return intValue;
                }
                else if (reader.TryGetDouble(out double doubleValue))
                {
                    return doubleValue;
                }
                break; // Could also handle other numeric types
            case JsonTokenType.True:
            case JsonTokenType.False:
                return reader.GetBoolean();
            case JsonTokenType.Null:
                return null;
            default:
                throw new JsonException($"Unexpected token: {reader.TokenType}");
        }

        return null;
    }

    private object ReadArray(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        var list = new List<object>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
            {
                return list;
            }

            list.Add(ReadObject(ref reader, options));
        }

        throw new JsonException("Expected EndArray token not found.");
    }

    public override void Write(Utf8JsonWriter writer, ExpandoObject value, JsonSerializerOptions options)
    {
        writer.WriteStartObject(); // Start writing the object

        foreach (var kvp in value)
        {
            writer.WritePropertyName(kvp.Key);
            WriteValue(writer, kvp.Value, options);
        }

        writer.WriteEndObject(); // End writing the object
    }

    private void WriteValue(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case ExpandoObject expando:
                Write(writer, expando, options); // Recursive call for nested objects
                break;
            case IList list:
                WriteArray(writer, list, options);
                break;
            case string str:
                writer.WriteStringValue(str);
                break;
            case bool boolVal:
                writer.WriteBooleanValue(boolVal);
                break;
            case int intVal:
                writer.WriteNumberValue(intVal);
                break;
            case long longVal:
                writer.WriteNumberValue(longVal);
                break;
            case float floatVal:
                writer.WriteNumberValue(floatVal);
                break;
            case double doubleVal:
                writer.WriteNumberValue(doubleVal);
                break;
            case DateTime dateTime:
                writer.WriteStringValue(dateTime);
                break;
            // Add other types as necessary
            default:
                JsonSerializer.Serialize(writer, value, value.GetType(), options);
                break;
        }
    }

    private void WriteArray(Utf8JsonWriter writer, IList list, JsonSerializerOptions options)
    {
        writer.WriteStartArray();

        foreach (var item in list)
        {
            WriteValue(writer, item, options);
        }

        writer.WriteEndArray();
    }
}