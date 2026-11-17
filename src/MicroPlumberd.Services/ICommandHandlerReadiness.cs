namespace MicroPlumberd.Services;

/// <summary>
/// The moment the app's command handlers can be relied on: from then on, every command appended to the app command
/// stream is handled (feature-017 design.md § slice B1).
/// </summary>
public interface ICommandHandlerReadiness
{
    /// <summary>
    /// Completes when every command appended to the app command stream from then on will be handled.
    /// Faults with the start's exception; is canceled when the service stops or is disposed first.
    /// </summary>
    Task WhenSubscribed { get; }
}
