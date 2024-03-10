using System.Text.Json;

namespace MicroPlumberd;

public readonly struct Metadata(Guid id, JsonElement data)
{
    public Guid Id => id;
    public JsonElement Data => data;
}
