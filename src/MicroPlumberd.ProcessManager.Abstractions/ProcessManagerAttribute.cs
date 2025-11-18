// ReSharper disable once CheckNamespace
namespace MicroPlumberd;

/// <summary>
/// Attribute used to mark a class as a process manager, enabling source generation for process manager infrastructure.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class ProcessManagerAttribute : Attribute { }