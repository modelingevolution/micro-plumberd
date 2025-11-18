using System.Text.Json;
using System.Text.Json.Serialization;

namespace MicroPlumberd.Encryption;

/// <summary>
/// JSON converter for serializing and deserializing SecretObject instances with automatic encryption and decryption.
/// </summary>
/// <typeparam name="T">The type of the secret value.</typeparam>
public class SecretObjectJsonConverter<T>(IEncryptor encryptor) : JsonConverter<SecretObject<T>>
{
    /// <summary>
    /// Reads and deserializes encrypted data into a SecretObject.
    /// </summary>
    /// <param name="reader">The JSON reader.</param>
    /// <param name="typeToConvert">The type being converted.</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>A SecretObject containing the encrypted data.</returns>
    public override SecretObject<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var dto = JsonSerializer.Deserialize<SecretObjectData>(ref reader, options);
        return SecretObject<T>.Load(dto.Data, dto.Recipient, encryptor);
    }

    /// <summary>
    /// Writes and serializes a SecretObject to JSON with encryption.
    /// </summary>
    /// <param name="writer">The JSON writer.</param>
    /// <param name="value">The SecretObject to serialize.</param>
    /// <param name="options">The serializer options.</param>
    public override void Write(Utf8JsonWriter writer, SecretObject<T> value, JsonSerializerOptions options)
    {
        using var scope = OperationContext.GetOrCreate(Flow.Request);
        byte[] data = encryptor.Encrypt(scope.Context,value.Value, value.Recipient);
        SecretObjectData dto = new SecretObjectData(value.Recipient,  data);
        JsonSerializer.Serialize(writer, dto, options);

    }
}