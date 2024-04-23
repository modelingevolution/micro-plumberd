namespace MicroPlumberd.Services;

/// <summary>
/// Attribute for marking a class suitable for command-handler code-generation.
/// The class should have methods with signature:
/// <code>
/// public async Task Handle(Guid id, YourFancyCommand cmd) { /* ... */ }
/// </code>
///
/// Where type of id can be any Parsable class/struct.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class CommandHandlerAttribute : Attribute
{
}
