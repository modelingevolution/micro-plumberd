using System.Text.Json;

namespace MicroPlumberd;

/// <summary>
/// Metadata structure.
/// </summary>
public readonly struct Metadata(Guid id,Guid eventId, long sourceStreamPosition, string sourceStreamId, JsonElement data)
{
    /// <summary>
    /// Gets the identifier or the stream. This is the second segment of the streamId (category-id).
    /// </summary>
    /// <value>
    /// The identifier.
    /// </value>
    public Guid Id => id;
    /// <summary>
    /// Data from metadata is deserialized in JsonElement.
    /// </summary>
    /// <value>
    /// The data.
    /// </value>
    public JsonElement Data => data;
    /// <summary>
    /// Gets the source stream position.
    /// </summary>
    /// <value>
    /// The source stream position.
    /// </value>
    public long SourceStreamPosition { get; } = sourceStreamPosition;
    /// <summary>
    /// Gets the full source stream-id
    /// </summary>
    /// <value>
    /// The source stream identifier.
    /// </value>
    public string SourceStreamId { get; } = sourceStreamId;
    /// <summary>
    /// Gets the event identifier.
    /// </summary>
    /// <value>
    /// The event identifier.
    /// </value>
    public Guid EventId => eventId;


}
