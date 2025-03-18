using System.Text.Json;

namespace MicroPlumberd;

/// <summary>
/// Object serializer used for event data and metadata serialization.
/// </summary>
public interface IObjectSerializer
{
    /// <summary>
    /// Deserializes the specified span for event's data.
    /// </summary>
    /// <param name="context"></param>
    /// <param name="span">The span.</param>
    /// <param name="t">The t.</param>
    /// <returns></returns>
    object? Deserialize(OperationContext context, ReadOnlySpan<byte> span, Type t);

    /// <summary>
    /// Parses span a JsonElement of the metadata.
    /// </summary>
    /// <param name="context"></param>
    /// <param name="span">The span.</param>
    /// <returns></returns>
    JsonElement ParseMetadata(OperationContext context, ReadOnlySpan<byte> span);

    /// <summary>
    /// Serializes the specified object.
    /// </summary>
    /// <param name="t">The t.</param>
    /// <returns></returns>
    byte[] Serialize(OperationContext context, object? t);
    /// <summary>
    /// Gets the type of the content. (application/json or application/octet-stream)
    /// </summary>
    /// <value>
    /// The type of the content.
    /// </value>
    string ContentType { get; }
}