namespace MicroPlumberd.Services.Identity.Aggregates;

/// <summary>
/// Exception thrown when a concurrency conflict is detected during aggregate state updates.
/// </summary>
public class ConcurrencyException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ConcurrencyException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the concurrency error.</param>
    public ConcurrencyException(string message) : base(message)
    {
    }
}