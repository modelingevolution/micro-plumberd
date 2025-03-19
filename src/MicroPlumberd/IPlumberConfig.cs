using Microsoft.Extensions.DependencyInjection;

namespace MicroPlumberd;

public interface IPlumberConfig : IExtension
{
    Func<Type, IObjectSerializer> SerializerFactory { get; set; }
    IConventions Conventions { get; }
    IServiceProvider ServiceProvider { get; set; }
    event Action<PlumberEngine> Created;

    void SetErrorHandlePolicy(Func<Exception, OperationContext, CancellationToken, Task<ErrorHandleDecision>> handler);
}

public interface IPlumberReadOnlyConfig : IExtension
{
    Func<Type, IObjectSerializer> SerializerFactory { get; }
    IReadOnlyConventions Conventions { get; }
    IServiceProvider ServiceProvider { get; }
    Task<ErrorHandleDecision> HandleError(Exception ex, OperationContext context, CancellationToken token);
}

public enum ErrorHandleDecision
{
    /// <summary>
    /// Will retry
    /// </summary>
    Retry,
    /// <summary>
    /// Will cancel subscription
    /// </summary>
    Cancel,
    /// <summary>
    /// Will ignore current error.
    /// </summary>
    Ignore,
    /// <summary>
    /// Logs error and shutdown the app
    /// </summary>
    FailFast
}