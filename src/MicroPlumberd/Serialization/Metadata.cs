using System.Text.Json;

namespace MicroPlumberd;

public readonly struct Metadata(Guid id,long sourceStreamPosition, string sourceStreamId, JsonElement data)
{
    public Guid Id => id;
    public JsonElement Data => data;
    public long SourceStreamPosition { get; } = sourceStreamPosition;
    public string SourceStreamId { get; } = sourceStreamId;
}
