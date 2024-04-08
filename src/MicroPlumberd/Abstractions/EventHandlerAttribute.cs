namespace MicroPlumberd;

/// <summary>
/// Attribute that marks a class that is an EventHandler for code-generation. This is usually a read-model or a processor.
/// </summary>
/// <seealso cref="System.Attribute" />
[AttributeUsage(AttributeTargets.Class)]
public class EventHandlerAttribute : Attribute { }

