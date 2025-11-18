namespace MicroPlumberd;

/// <summary>
/// Represents a fault exception with strongly-typed data.
/// </summary>
/// <typeparam name="TData">The type of the fault data.</typeparam>
public class FaultException<TData> : FaultException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FaultException{TData}"/> class with a message, data, and code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="data">The fault data.</param>
    /// <param name="code">The error code.</param>
    public FaultException(string? message, TData data, int code) : base(message)
    {
        Data = data;
        Code = code;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FaultException{TData}"/> class with data and code.
    /// </summary>
    /// <param name="data">The fault data.</param>
    /// <param name="code">The error code.</param>
    public FaultException(TData data, int code) : base(data.ToString())
    {
        Data = data;
        Code = code;
    }

    /// <summary>
    /// Gets the fault data.
    /// </summary>
    public TData Data { get; init; }

    /// <summary>
    /// Gets the fault data as an object.
    /// </summary>
    /// <returns>The fault data.</returns>
    public override object GetFaultData() => (object)this.Data;

    /// <summary>
    /// Initializes a new instance of the <see cref="FaultException{TData}"/> class with data.
    /// </summary>
    /// <param name="data">The fault data.</param>
    public FaultException(TData data) => this.Data = data;
}

/// <summary>
/// Attribute to indicate that a method or class throws a fault exception of a specific message type.
/// </summary>
/// <typeparam name="TMessage">The type of the fault message.</typeparam>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public class ThrowsFaultExceptionAttribute<TMessage>() : ThrowsFaultExceptionAttribute(typeof(TMessage));

/// <summary>
/// Base attribute class to indicate that a method or class throws a fault exception.
/// </summary>
/// <param name="thrownType">The type of exception that can be thrown.</param>
public abstract class ThrowsFaultExceptionAttribute(Type thrownType) : Attribute
{
    /// <summary>
    /// Gets the type of exception that can be thrown.
    /// </summary>
    public Type ThrownType { get; init; } = thrownType;
}

/// <summary>
/// Represents a base fault exception.
/// </summary>
public class FaultException : Exception
{
    /// <summary>
    /// Gets the error code.
    /// </summary>
    public int Code { get; init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="FaultException"/> class.
    /// </summary>
    public FaultException()
    {
    }

    /// <summary>
    /// Creates a new fault exception with the specified message, data, and code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="data">The fault data.</param>
    /// <param name="code">The error code.</param>
    /// <returns>A new fault exception instance.</returns>
    public static FaultException Create(string message, object data, int code)
    {
        return (FaultException)Activator.CreateInstance(typeof(FaultException<>).MakeGenericType(data.GetType()), message, data, code)!;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FaultException"/> class with a message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public FaultException(string? message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FaultException"/> class with a message and code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="code">The error code.</param>
    public FaultException(string? message, int code) : base(message)
    {
        Code = code;
    }

    /// <summary>
    /// Gets the fault data as an object. Returns null for the base fault exception.
    /// </summary>
    /// <returns>The fault data, or null.</returns>
    public virtual object GetFaultData() => null;
}