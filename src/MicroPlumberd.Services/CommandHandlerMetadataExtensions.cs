namespace MicroPlumberd.Services;

/// <summary>
/// Provides extension methods for accessing command handler metadata.
/// </summary>
public static class CommandHandlerMetadataExtensions
{
    /// <summary>
    /// Retrieves the recipient ID from the command metadata.
    /// </summary>
    /// <param name="m">The metadata containing the recipient ID.</param>
    /// <returns>The recipient ID as a string.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the RecipientId is not found in the metadata.</exception>
    public static string RecipientId(this Metadata m) => m.TryGetValue<string>("RecipientId", out var v)
        ? v
        : throw new InvalidOperationException("RecipientId not found in command");
}