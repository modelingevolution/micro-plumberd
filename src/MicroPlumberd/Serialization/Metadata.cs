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


    /// <summary>
    /// Extracts and parses the stream identifier.
    /// </summary>
    /// <typeparam name="T">The type to which the stream identifier will be parsed. Must implement <see cref="IParsable{T}"/>.</typeparam>
    /// <returns>The parsed stream identifier as an instance of type <typeparamref name="T"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown if the parsed identifier is null or invalid for type <typeparamref name="T"/>.</exception>
    /// <exception cref="FormatException">Thrown if the format of the identifier is invalid for type <typeparamref name="T"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if the <see cref="SourceStreamId"/> does not contain a valid separator ('-').</exception>
    public T StreamId<T>() where T : IParsable<T>
    {
        if (Data.TryGetProperty("SourceStreamId", out var j) && j.ValueKind == JsonValueKind.String)
            return StreamIdFromStr<T>(j.GetString()!);

        return StreamIdFromStr<T>(SourceStreamId);
    }

    private static T StreamIdFromStr<T>(string str) where T : IParsable<T>
    {
        int index = str.IndexOf('-');
        string id = str.Substring(index + 1);
        return T.Parse(id, null);
    }
}
