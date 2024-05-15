using System.Text.Json;
using System.Text.Json.Serialization;

namespace MicroPlumberd.Encryption;

public class SecretObjectJsonConverter<T>(IEncryptor encryptor) : JsonConverter<SecretObject<T>>
{
    public override SecretObject<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var dto = JsonSerializer.Deserialize<SecretObjectData>(ref reader, options);
        return SecretObject<T>.Load(dto.Data, dto.Recipient, encryptor);
    }

    public override void Write(Utf8JsonWriter writer, SecretObject<T> value, JsonSerializerOptions options)
    {
        byte[] data = encryptor.Encrypt(value.Value, value.Recipient);
        SecretObjectData dto = new SecretObjectData(value.Recipient,  data);
        JsonSerializer.Serialize(writer, dto, options);
    }
}