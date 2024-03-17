namespace MicroPlumberd.Services;

public static class CommandHandlerMetadataExtensions
{
    public static Guid RecipientId(this Metadata m) => m.TryGetValue<Guid>("RecipientId", out var v)
        ? v
        : throw new InvalidOperationException("RecipientId not found in command");
}