using System.Text.Json;

namespace MicroPlumberd;

public interface IObjectSerializer
{
    object? Deserialize(ReadOnlySpan<byte> span, Type t);
    JsonElement ParseMetadata(ReadOnlySpan<byte> span);
    byte[] Serialize(object? t);
    string ContentType { get; }
}