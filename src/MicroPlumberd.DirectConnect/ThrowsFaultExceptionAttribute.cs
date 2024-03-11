namespace MicroPlumberd.DirectConnect;

[AttributeUsage(AttributeTargets.Method)]
public class ThrowsFaultExceptionAttribute<TMessage> : Attribute;