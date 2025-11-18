using Microsoft.Extensions.DependencyInjection;

namespace MicroPlumberd;

/// <summary>
/// Provides configuration options for PlumberEngine instances.
/// </summary>
public interface IPlumberConfig : IExtension
{
    /// <summary>
    /// Gets or sets the factory for creating serializers for different types.
    /// </summary>
    Func<Type, IObjectSerializer> SerializerFactory { get; set; }

    /// <summary>
    /// Gets the conventions configuration.
    /// </summary>
    IConventions Conventions { get; }

    /// <summary>
    /// Gets or sets the service provider for dependency injection.
    /// </summary>
    IServiceProvider ServiceProvider { get; set; }

    /// <summary>
    /// Event raised when a PlumberEngine instance is created.
    /// </summary>
    event Action<PlumberEngine> Created;

    /// <summary>
    /// Sets the error handling policy for subscription errors.
    /// </summary>
    /// <param name="handler">The error handling function.</param>
    void SetErrorHandlePolicy(Func<Exception, OperationContext, CancellationToken, Task<ErrorHandleDecision>> handler);
}

/// <summary>
/// Provides read-only access to PlumberEngine configuration.
/// </summary>
public interface IPlumberReadOnlyConfig : IExtension
{
    /// <summary>
    /// Gets the factory for creating serializers for different types.
    /// </summary>
    Func<Type, IObjectSerializer> SerializerFactory { get; }

    /// <summary>
    /// Gets the read-only conventions configuration.
    /// </summary>
    IReadOnlyConventions Conventions { get; }

    /// <summary>
    /// Gets the service provider for dependency injection.
    /// </summary>
    IServiceProvider ServiceProvider { get; }

    /// <summary>
    /// Handles an error that occurred during event processing.
    /// </summary>
    /// <param name="ex">The exception that occurred.</param>
    /// <param name="context">The operation context.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>The decision on how to handle the error.</returns>
    Task<ErrorHandleDecision> HandleError(Exception ex, OperationContext context, CancellationToken token);
}

/// <summary>
/// Defines how errors should be handled during event processing.
/// </summary>
public enum ErrorHandleDecision
{
    /// <summary>
    /// Retry the operation that caused the error.
    /// </summary>
    Retry,

    /// <summary>
    /// Cancel the subscription and stop processing events.
    /// </summary>
    Cancel,

    /// <summary>
    /// Ignore the current error and continue processing.
    /// </summary>
    Ignore,

    /// <summary>
    /// Log the error and perform a fail-fast shutdown of the application.
    /// </summary>
    FailFast
}