using Microsoft.Extensions.DependencyInjection;

namespace MicroPlumberd;

public interface IPlumberConfig : IExtension
{
    Func<Type, IObjectSerializer> SerializerFactory { get; set; }
    IConventions Conventions { get; }
    IServiceProvider ServiceProvider { get; set; }
    event Action<IPlumber> Created;

    void SetErrorHandlePolicy(Func<Exception, string, CancellationToken, Task<ErrorHandleDecision>> handler);
}

public interface IPlumberReadOnlyConfig : IExtension
{
    Func<Type, IObjectSerializer> SerializerFactory { get; }
    IReadOnlyConventions Conventions { get; }
    IServiceProvider ServiceProvider { get; }
    Task<ErrorHandleDecision> HandleError(Exception ex, string streamName, CancellationToken token);
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
    Ignore
}