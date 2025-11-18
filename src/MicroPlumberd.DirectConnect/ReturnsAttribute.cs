namespace MicroPlumberd.DirectConnect;

/// <summary>
/// Base attribute for marking commands with their return types.
/// This attribute is used to register return types for proper serialization and type handling in DirectConnect.
/// </summary>
/// <param name="returnType">The type that the command returns.</param>
public abstract class ReturnsAttribute(Type returnType) : Attribute
{
    /// <summary>
    /// Gets the type that the attributed command returns.
    /// </summary>
    public Type ReturnType { get; init; } = returnType;
}