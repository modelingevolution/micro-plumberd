using ModelingEvolution.DirectConnect;

namespace MicroPlumberd.DirectConnect;

/// <summary>
/// Generic attribute for marking commands with their strongly-typed return type.
/// Apply this attribute to command classes to specify what type they return when executed.
/// </summary>
/// <typeparam name="TResult">The type of result the command returns.</typeparam>
[AttributeUsage(AttributeTargets.Class)]
public class ReturnsAttribute<TResult>() : ReturnsAttribute(typeof(TResult)) { }