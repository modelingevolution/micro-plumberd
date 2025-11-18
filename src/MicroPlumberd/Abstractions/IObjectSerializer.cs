using System.Text.Json;

namespace MicroPlumberd;

/// <summary>
/// Object serializer used for event data and metadata serialization.
/// </summary>
public interface IObjectSerializer
{
    /// <summary>
    /// Deserializes the specified byte span into an object of the given type.
    /// </summary>
    /// <param name="context">The operation context containing request-scoped information.</param>
    /// <param name="span">The byte span containing the serialized event data.</param>
    /// <param name="t">The target type to deserialize into.</param>
    /// <returns>The deserialized object, or <c>null</c> if the span is empty.</returns>
    object? Deserialize(OperationContext context, ReadOnlySpan<byte> span, Type t);

    /// <summary>
    /// Parses the metadata byte span into a <see cref="JsonElement"/>.
    /// </summary>
    /// <param name="context">The operation context containing request-scoped information.</param>
    /// <param name="span">The byte span containing the serialized metadata.</param>
    /// <returns>A <see cref="JsonElement"/> representing the parsed metadata.</returns>
    JsonElement ParseMetadata(OperationContext context, ReadOnlySpan<byte> span);

    /// <summary>
    /// Serializes the specified object into a byte array.
    /// </summary>
    /// <param name="context">The operation context containing request-scoped information.</param>
    /// <param name="t">The object to serialize.</param>
    /// <returns>A byte array containing the serialized object data.</returns>
    byte[] Serialize(OperationContext context, object? t);
    /// <summary>
    /// Gets the type of the content. (application/json or application/octet-stream)
    /// </summary>
    /// <value>
    /// The type of the content.
    /// </value>
    string ContentType { get; }
}