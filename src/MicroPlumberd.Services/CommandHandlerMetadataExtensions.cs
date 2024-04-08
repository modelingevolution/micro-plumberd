namespace MicroPlumberd.Services;

public static class CommandHandlerMetadataExtensions
{
    public static string RecipientId(this Metadata m) => m.TryGetValue<string>("RecipientId", out var v)
        ? v
        : throw new InvalidOperationException("RecipientId not found in command");
}