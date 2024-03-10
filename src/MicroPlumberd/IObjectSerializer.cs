using System.Text.Json;

namespace MicroPlumberd;

public interface IObjectSerializer
{
    object? Deserialize(ReadOnlySpan<byte> span, Type t);
    JsonElement Parse(ReadOnlySpan<byte> span);
    byte[] SerializeToUtf8Bytes(object? t);
}