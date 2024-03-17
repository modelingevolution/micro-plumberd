namespace MicroPlumberd.DirectConnect;

public abstract class ReturnsAttribute(Type returnType) : Attribute
{
    public Type ReturnType { get; init; } = returnType;
}