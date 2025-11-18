using System.Diagnostics;
using System.Dynamic;
using Grpc.Core;
using MicroPlumberd;

/// <summary>
/// Defines the execution flow context types for operations in the system.
/// </summary>
public enum Flow
{
    /// <summary>
    /// Flow from a component, for instance BlazorUI Server-Side.
    /// </summary>
    Component,

    /// <summary>
    /// Flow in command-handler (scope/singleton).
    /// </summary>
    CommandHandler,

    /// <summary>
    /// Flow in event-handler (scope/singleton).
    /// </summary>
    EventHandler,

    /// <summary>
    /// Flow in API, for instance REST/GRPC/etc.
    /// </summary>
    Request
}

/// <summary>
/// Provides extension methods for managing standard operation context properties.
/// </summary>
public static class StandardOperationContextExtensions
{
    internal static void OnCommandHandlerBegin(OperationContext context) {}
    internal static void OnCommandHandlerEnd(OperationContext context) { }

    internal static void OnEventHandlerBegin(OperationContext context) { }
    internal static void OnEventHandlerEnd(OperationContext context) { }

    /// <summary>
    /// Sets the correlation ID in the operation context.
    /// </summary>
    /// <param name="context">The operation context.</param>
    /// <param name="id">The correlation ID to set.</param>
    /// <returns>The operation context for method chaining.</returns>
    public static OperationContext SetCorrelationId(this OperationContext context, Guid? id)
    {
        if (id.HasValue)
            context.SetValue(OperationContextProperty.CorrelationId, id.Value);
        return context;
    }
    /// <summary>
    /// Sets the stream name in the operation context.
    /// </summary>
    /// <param name="context">The operation context.</param>
    /// <param name="streamName">The stream name to set.</param>
    public static void SetStreamName(this OperationContext context, string? streamName)
    {
        if (!string.IsNullOrWhiteSpace(streamName))
            context.SetValue(OperationContextProperty.StreamName, streamName);

    }
    /// <summary>
    /// Sets the causation ID in the operation context.
    /// </summary>
    /// <param name="context">The operation context.</param>
    /// <param name="id">The causation ID to set.</param>
    /// <returns>The operation context for method chaining.</returns>
    public static OperationContext SetCausationId(this OperationContext context, Guid? id)
    {
        if (id.HasValue)
            context.SetValue(OperationContextProperty.CausactionId, id.Value);
        return context;

    }
    /// <summary>
    /// Gets the causation ID from the operation context.
    /// </summary>
    /// <param name="context">The operation context.</param>
    /// <returns>The causation ID if set; otherwise, <c>null</c>.</returns>
    public static Guid? GetCausationId(this OperationContext context)
    {
        return context.TryGetValue<Guid>(OperationContextProperty.CausactionId, out var id) ? id : null;
    }
    /// <summary>
    /// Sets the user ID in the operation context.
    /// </summary>
    /// <param name="context">The operation context.</param>
    /// <param name="user">The user ID to set.</param>
    /// <returns>The operation context for method chaining.</returns>
    public static OperationContext SetUserId(this OperationContext context, string? user)
    {
        if (!string.IsNullOrEmpty(user)) context.SetValue(OperationContextProperty.UserId, user);
        return context;
    }
    /// <summary>
    /// Gets the user ID from the operation context.
    /// </summary>
    /// <param name="context">The operation context.</param>
    /// <returns>The user ID if set; otherwise, <c>null</c>.</returns>
    public static string? GetUserId(this OperationContext context)
    {
        return context.TryGetValue<string>(OperationContextProperty.UserId, out var user) ? user : null;
    }
    /// <summary>
    /// Gets the correlation ID from the operation context.
    /// </summary>
    /// <param name="context">The operation context.</param>
    /// <returns>The correlation ID if set; otherwise, <c>null</c>.</returns>
    public static Guid? GetCorrelationId(this OperationContext context)
    {
        return context.TryGetValue<Guid>(OperationContextProperty.CorrelationId, out var id) ? id : null;
    }
   
}

/// <summary>
///  Used in configuration, invoked by CommandService and EventService as well as Plumber (scoped) and PlumberInstance (singleton)
/// </summary>
/// <param name="context"></param>
public delegate void OperationContextHandler(OperationContext context);

/// <summary>
/// Represents a property key in the operation context with metadata tracking.
/// </summary>
/// <param name="Name">The name of the property.</param>
/// <param name="IsMetadata">Indicates whether this property should be included in event metadata.</param>
[DebuggerDisplay("{Name}")]
public readonly record struct OperationContextProperty(string Name, bool IsMetadata=true) : IComparable<OperationContextProperty>
{
    private static ulong _counter = 0;

    /// <summary>
    /// Gets the unique identifier for this property instance.
    /// </summary>
    public ulong Id { get; } = Interlocked.Increment(ref _counter);

    /// <summary>
    /// Implicitly converts a string to an <see cref="OperationContextProperty"/>.
    /// </summary>
    /// <param name="key">The property name.</param>
    public static implicit operator OperationContextProperty(string key) => new OperationContextProperty(key);

    /// <summary>
    /// Standard property for correlation ID.
    /// </summary>
    public readonly static OperationContextProperty CorrelationId = "$correlationId";

    /// <summary>
    /// Standard property for causation ID.
    /// </summary>
    public readonly static OperationContextProperty CausactionId = "$causationId";

    /// <summary>
    /// Standard property for user ID.
    /// </summary>
    public readonly static OperationContextProperty UserId = "UserId";

    /// <summary>
    /// Standard property for stream name (not included in metadata).
    /// </summary>
    public readonly static OperationContextProperty StreamName = new OperationContextProperty("StreamName", false);

    /// <summary>
    /// Standard property for aggregate type (not included in metadata).
    /// </summary>
    public readonly static OperationContextProperty AggregateType = new OperationContextProperty("AggregateType",false);

    /// <summary>
    /// Standard property for aggregate ID (not included in metadata).
    /// </summary>
    public readonly static OperationContextProperty AggregateId = new OperationContextProperty("AggregateId",false);

    /// <summary>
    /// Gets the maximum property ID assigned so far.
    /// </summary>
    public static ulong Max => _counter;


    /// <summary>
    /// Compares this property to another based on their unique IDs.
    /// </summary>
    /// <param name="other">The other property to compare to.</param>
    /// <returns>A value indicating the relative order of the properties.</returns>
    public int CompareTo(OperationContextProperty other)
    {
        return Id.CompareTo(other.Id);
    }
}
/// <summary>
/// Represents context for an operation in the system.
/// Provides both AsyncLocal and DI-based context propagation.
/// </summary>
public class OperationContext
{
    // AsyncLocal storage for background processes
    private static readonly AsyncLocal<OperationContext?> _asyncLocalContext = new();

    // Dictionary for storing context values
    // TODO: we can recycle it.
    private readonly SortedList<OperationContextProperty, object> _values = new();
    private readonly Flow _flow;
    
    /// <summary>
    /// Gets the current operation context from AsyncLocal storage.
    /// This is primarily used for background processes or when DI is not available.
    /// </summary>
    public static OperationContext? Current => _asyncLocalContext.Value;

    /// <summary>
    /// Creates a new OperationContext.
    /// </summary>
    internal OperationContext(Flow flow)
    {
        _flow = flow;
    }

    /// <summary>
    /// Gets the execution flow type for this operation context.
    /// </summary>
    public Flow Flow => _flow;

    /// <summary>
    /// Gets or creates an operation context with asynchronous initialization.
    /// </summary>
    /// <param name="OnFlow">Function to determine the flow type.</param>
    /// <param name="OnCreate">Asynchronous initialization function for the new context.</param>
    /// <returns>A context scope containing the operation context.</returns>
    public static async Task<ContextScope> GetOrCreate(Func<ValueTask<Flow>> OnFlow, Func<OperationContext,Task> OnCreate)
    {
        if (Current != null)
            return new ContextScope(Current, false);

        OperationContext c = new OperationContext(await OnFlow());
        await OnCreate(c);
        _asyncLocalContext.Value = c;
        return new ContextScope(c, true);
    }

    /// <summary>
    /// Gets the current operation context or creates a new one with the specified flow type.
    /// </summary>
    /// <param name="flow">The flow type for the new context.</param>
    /// <returns>A context scope containing the operation context.</returns>
    public static ContextScope GetOrCreate(Flow flow)
    {
        if (Current != null)
            return new ContextScope(Current, false);

        OperationContext c = new OperationContext(flow);
        _asyncLocalContext.Value = c;
        return new ContextScope(c, true);
    }

    /// <summary>
    /// Creates a new operation context or returns the current one.
    /// </summary>
    /// <param name="flow">The flow type for the new context.</param>
    /// <returns>The operation context.</returns>
    public static OperationContext Create(Flow flow)
    {
        if (Current == null)
        {
            // this is a direct flow;
            return new OperationContext(Flow.Component);
        }

        return Current;
    }

    /// <summary>
    /// Creates a new operation context or returns the current one. Used by dependency injection.
    /// </summary>
    /// <returns>The operation context.</returns>
    /// <exception cref="InvalidOperationException">Thrown when nested operation context is detected in direct flow.</exception>
    public static OperationContext Create()
    {
        if (Current == null)
        {
            // this is a direct flow;
            return new OperationContext(Flow.Component);
        }

        if (Current._flow == Flow.Component)
            throw new InvalidOperationException("Detected nested operation-context in direct flow is unsupported.");

        return Current;
    }
    /// <summary>
    /// Creates a new OperationContext with initial values from an existing context.
    /// </summary>
    /// <param name="source">Source context to copy values from.</param>
    /// <param name="flow">The flow type for the new context.</param>
    internal OperationContext(OperationContext? source, Flow flow)
    {
        _flow = flow;

        if (source != null)
        {
            ImportFrom(source);
        }
    }

    /// <summary>
    /// Sets a strongly-typed value in the context.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="key">The property key.</param>
    /// <param name="value">The value to set.</param>
    public void SetValue<T>(OperationContextProperty key, T value)
    {
        _values[key] = value;
    }

    /// <summary>
    /// Gets a strongly-typed value from the context.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="key">The property key.</param>
    /// <returns>The value if found and of the correct type; otherwise, the default value for the type.</returns>
    public T GetValue<T>(OperationContextProperty key)
    {
        if (_values.TryGetValue(key, out var value) && value is T typedValue)
        {
            return typedValue;
        }
        return default;
    }

    /// <summary>
    /// Tries to get a strongly-typed value from the context.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="key">The property key.</param>
    /// <param name="value">When this method returns, contains the value if found and of the correct type; otherwise, the default value.</param>
    /// <returns><c>true</c> if the value was found and is of the correct type; otherwise, <c>false</c>.</returns>
    public bool TryGetValue<T>(OperationContextProperty key, out T value)
    {
        if (_values.TryGetValue(key, out var obj) && obj is T typedValue)
        {
            value = typedValue;
            return true;
        }
        value = default;
        return false;
    }

    /// <summary>
    /// Clears the current AsyncLocal context.
    /// </summary>
    public static void ClearContext()
    {
        _asyncLocalContext.Value = null;
    }

    /// <summary>
    /// Creates a context scope that can be disposed.
    /// </summary>
    /// <returns>A disposable context scope.</returns>
    /// <exception cref="InvalidOperationException">Thrown when attempting to create a nested scope.</exception>
    public ContextScope CreateScope()
    {
        if (_asyncLocalContext.Value != null) throw new InvalidOperationException("Cannot create nested scope!");
        _asyncLocalContext.Value = this;
        return new ContextScope();
    }

    /// <summary>
    /// Imports values from another context.
    /// </summary>
    /// <param name="source">The source context to import values from.</param>
    public void ImportFrom(OperationContext source)
    {
        foreach (var kvp in source._values)
        {
            _values[kvp.Key] = kvp.Value;
        }
    }

    /// <summary>
    /// Copies metadata values to a dynamic object dictionary.
    /// </summary>
    /// <param name="dstObj">The destination dynamic object that implements <see cref="IDictionary{TKey, TValue}"/>.</param>
    public void CopyTo(dynamic dstObj)
    {
        var dst = (IDictionary<string, object>)dstObj;
        foreach (var i in this._values.Where(i => i.Key.IsMetadata && i.Value != null!))
        {
            dst[i.Key.Name] = i.Value;
        }
    }

    /// <summary>
    /// Clears all values in the context.
    /// </summary>
    public void Clear()
    {
        _values.Clear();
    }

    /// <summary>
    /// Represents a disposable scope for an operation context.
    /// </summary>
    /// <param name="cx">The operation context.</param>
    /// <param name="scoped">Indicates whether the scope should clear the AsyncLocal context on disposal.</param>
    public readonly record struct ContextScope(OperationContext cx, bool scoped) : IDisposable
    {
        /// <summary>
        /// Gets the operation context associated with this scope.
        /// </summary>
        public OperationContext Context => cx;

        /// <summary>
        /// Disposes the context scope, clearing the AsyncLocal context if this is a scoped instance.
        /// </summary>
        public void Dispose()
        {
            if (scoped)
                _asyncLocalContext.Value = null;
        }
    }
}