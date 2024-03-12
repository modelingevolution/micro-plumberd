namespace MicroPlumberd.DirectConnect;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class ThrowsFaultExceptionAttribute<TMessage>() : ThrowsFaultExceptionAttribute(typeof(TMessage));

public abstract class ThrowsFaultExceptionAttribute(Type thrownType) : Attribute
{
    public Type ThrownType { get; init; } = thrownType;
}
