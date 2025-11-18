using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Dynamic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MicroPlumberd;

class OptionJsonConverter<T> : JsonConverter<Option<T>>
{
    public override Option<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return new Option<T> { IsDefined = true };

        return JsonSerializer.Deserialize<T>(ref reader, options);
    }

    public override void Write(Utf8JsonWriter writer, Option<T> value, JsonSerializerOptions options)
    {
        if (!value.IsDefined)
        {
            writer.WriteNullValue();
        }
        else
        {
            JsonSerializer.Serialize(writer, value.Value, options);
        }
    }
}
/// <summary>
/// Json Option factory used to support Option&lt;T&gt; struct deserialization/serialization.
/// </summary>
/// <seealso cref="System.Text.Json.Serialization.JsonConverterFactory" />
public class OptionConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        if (!typeToConvert.IsGenericType) return false;

        return typeToConvert.GetGenericTypeDefinition() == typeof(Option<>);
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        Type valueType = typeToConvert.GetGenericArguments()[0];

        var specificType = typeof(OptionJsonConverter<>).MakeGenericType(valueType);
        JsonConverter converter = (JsonConverter)Activator.CreateInstance(specificType)!;

        return converter;
    }
}

class OptionTypeConverter<T> : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
    {
        if (sourceType == typeof(T))
        {
            return true;
        }

        return base.CanConvertFrom(context, sourceType);
    }

    public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
    {
        if (value is T s)
            return new Option<T> { Value = s, IsDefined = true };

        return base.ConvertFrom(context, culture, value);
    }
}


/// <summary>
/// For command/event properties useful to get rid of property-sourcing.
/// Represents an optional value that can be defined or undefined.
/// </summary>
/// <typeparam name="T">The type of the optional value.</typeparam>
public readonly record struct Option<T> 
{
    /// <summary>
    /// Gets an empty (undefined) option.
    /// </summary>
    public static Option<T> Empty { get; } = new Option<T>();

    /// <summary>
    /// Gets the value of the option.
    /// </summary>
    public T Value { get; init; }

    /// <summary>
    /// Implicitly converts an option to its underlying value.
    /// </summary>
    /// <param name="v">The option to convert.</param>
    /// <exception cref="InvalidOperationException">Thrown when the option is not defined.</exception>
    public static implicit operator T(Option<T> v)
    {
        if (!v.IsDefined) throw new InvalidOperationException("Option was not defined.");

        return v.Value;
    }

    /// <summary>
    /// Implicitly converts a value to an option.
    /// </summary>
    /// <param name="value">The value to wrap in an option.</param>
    public static implicit operator Option<T>(T value) => new Option<T>() { Value = value, IsDefined = true };

    /// <summary>
    /// Gets a value indicating whether the option is defined (has a value).
    /// </summary>
    public bool IsDefined { get; init; }

}

/// <summary>
/// JSON-based implementation of IObjectSerializer using System.Text.Json.
/// </summary>
public sealed class JsonObjectSerializer : IObjectSerializer
{
    /// <summary>
    /// Gets the default JSON serializer options used by this serializer.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new() { Converters = { new ExpandoObjectConverter(), new OptionConverterFactory() } };

    private static JsonElement Empty = JsonSerializer.Deserialize<JsonElement>("{}");

    /// <inheritdoc/>
    public object? Deserialize(OperationContext context, ReadOnlySpan<byte> span, Type t)
    {
        return JsonSerializer.Deserialize(span, t, Options);
    }

    /// <inheritdoc/>
    public JsonElement ParseMetadata(OperationContext context, ReadOnlySpan<byte> span)
    {
        if(span.Length == 0) return Empty;
        return JsonSerializer.Deserialize<JsonElement>(span, Options);
    }

    /// <inheritdoc/>
    public byte[] Serialize(OperationContext context, object? t)
    {
        return t == null ? Array.Empty<byte>() : JsonSerializer.SerializeToUtf8Bytes(t, t.GetType(), Options);
    }

    /// <inheritdoc/>
    public string ContentType => "application/json";
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
        IDictionary<string, object> dictionary = expando!;

        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.EndObject:
                    return expando;
                case JsonTokenType.PropertyName:
                    var propertyName = reader.GetString();
                    reader.Read();
                    dictionary[propertyName!] = ReadObject(ref reader, options)!;
                    break;
                default:
                    throw new JsonException();
            }
        }

        throw new JsonException("Expected EndObject token not found.");
    }

    private object? ReadObject(ref Utf8JsonReader reader, JsonSerializerOptions options)
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

            list.Add(ReadObject(ref reader, options)!);
        }

        throw new JsonException("Expected EndArray token not found.");
    }

    public override void Write(Utf8JsonWriter writer, ExpandoObject value, JsonSerializerOptions options)
    {
        writer.WriteStartObject(); // Start writing the object

        foreach (var kvp in value)
        {
            writer.WritePropertyName(kvp.Key);
            WriteValue(writer, kvp.Value!, options);
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