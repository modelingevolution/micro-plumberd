using Microsoft.Extensions.DependencyInjection;

namespace MicroPlumberd.Services;

/// <summary>
/// Defines a contract for command handlers to register their types and handlers with the dependency injection container.
/// This interface is typically implemented by source-generated code.
/// </summary>
public interface IServiceTypeRegister
{
    /// <summary>
    /// Gets the collection of return types that the command handler can produce.
    /// </summary>
    static abstract IEnumerable<Type> ReturnTypes { get; }

    /// <summary>
    /// Gets the collection of fault exception types that the command handler can throw.
    /// </summary>
    static abstract IEnumerable<Type> FaultTypes { get; }

    /// <summary>
    /// Gets the collection of command types that the command handler can process.
    /// </summary>
    static abstract IEnumerable<Type> CommandTypes { get; }

    /// <summary>
    /// Registers the command handlers with the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection to register handlers with.</param>
    /// <param name="scoped">If true, registers handlers as scoped services; otherwise, as singleton services.</param>
    /// <returns>The service collection for method chaining.</returns>
    static abstract IServiceCollection RegisterHandlers(IServiceCollection services, bool scoped = true);
}