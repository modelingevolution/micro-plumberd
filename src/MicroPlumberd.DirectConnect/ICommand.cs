using ModelingEvolution.DirectConnect;

namespace MicroPlumberd.DirectConnect;

[AttributeUsage(AttributeTargets.Class)]
public class ReturnsAttribute<TResult>() : ReturnsAttribute(typeof(TResult)) { }