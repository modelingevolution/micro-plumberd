using System.Diagnostics;
using System.Dynamic;
using Grpc.Core;
using MicroPlumberd;

public enum Flow
{
    // Flow from a component, for instance BlazorUI Server-Side.
    Component, 
    
    // Flow in command-handler (scope/singleton)
    CommandHandler,

    // Flow in evnet-handler (scope/singleton)
    EventHandler,
    
    // Flow in API, for instance REST/GRPC/etc
    Request
}

public static class StandardOperationContextExtensions
{
    internal static void OnCommandHandlerBegin(OperationContext context) {}
    internal static void OnCommandHandlerEnd(OperationContext context) { }

    internal static void OnEventHandlerBegin(OperationContext context) { }
    internal static void OnEventHandlerEnd(OperationContext context) { }
    public static OperationContext SetCorrelationId(this OperationContext context, Guid? id)
    {
        if (id.HasValue)
            context.SetValue(OperationContextProperty.CorrelationId, id.Value);
        return context;
    }
    public static void SetStreamName(this OperationContext context, string? streamName)
    {
        if (!string.IsNullOrWhiteSpace(streamName))
            context.SetValue(OperationContextProperty.StreamName, streamName);

    }
    public static OperationContext SetCausationId(this OperationContext context, Guid? id)
    {
        if (id.HasValue)
            context.SetValue(OperationContextProperty.CausactionId, id.Value);
        return context;

    }
    public static Guid? GetCausationId(this OperationContext context)
    {
        return context.TryGetValue<Guid>(OperationContextProperty.CausactionId, out var id) ? id : null;
    }
    public static OperationContext SetUserId(this OperationContext context, string? user)
    {
        if (!string.IsNullOrEmpty(user)) context.SetValue(OperationContextProperty.UserId, user);
        return context;
    }
    public static string? GetUserId(this OperationContext context)
    {
        return context.TryGetValue<string>(OperationContextProperty.UserId, out var user) ? user : null;
    }
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

[DebuggerDisplay("{Name}")]
public readonly record struct OperationContextProperty(string Name, bool IsMetadata=true) : IComparable<OperationContextProperty>
{
    private static ulong _counter = 0;
    public ulong Id { get; } = Interlocked.Increment(ref _counter);

    public static implicit operator OperationContextProperty(string key) => new OperationContextProperty(key);

    public readonly static OperationContextProperty CorrelationId = "$correlationId";
    public readonly static OperationContextProperty CausactionId = "$causationId";
    public readonly static OperationContextProperty UserId = "UserId";
    public readonly static OperationContextProperty StreamName = new OperationContextProperty("StreamName", false);
    public readonly static OperationContextProperty AggregateType = new OperationContextProperty("AggregateType",false);
    public readonly static OperationContextProperty AggregateId = new OperationContextProperty("AggregateId",false);

    public static ulong Max => _counter;


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

    public Flow Flow => _flow;

    public static ContextScope GetOrCreate(Flow flow)
    {
        if (Current != null)
            return new ContextScope(Current, false);

        OperationContext c = new OperationContext(flow);
        _asyncLocalContext.Value = c;
        return new ContextScope(c, true);
    }
    public static OperationContext Create(Flow flow)
    {
        if (Current == null)
        {
            // this is a direct flow;
            return new OperationContext(Flow.Component);
        }

        return Current;
    }
    // Used by di.
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
    public void SetValue<T>(OperationContextProperty key, T value)
    {
        _values[key] = value;
    }

    /// <summary>
    /// Gets a strongly-typed value from the context.
    /// </summary>
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

    public static void ClearContext()
    {
        _asyncLocalContext.Value = null;
    }

    /// <summary>
    /// Creates a context scope that can be disposed.
    /// </summary>
    public ContextScope CreateScope()
    {
        if (_asyncLocalContext.Value != null) throw new InvalidOperationException("Cannot create nested scope!");
        _asyncLocalContext.Value = this;
        return new ContextScope();
    }

    /// <summary>
    /// Imports values from another context.
    /// </summary>
    public void ImportFrom(OperationContext source)
    {
        foreach (var kvp in source._values)
        {
            _values[kvp.Key] = kvp.Value;
        }
    }


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

    // Nested class for managing context scope
    public readonly record struct ContextScope(OperationContext cx,bool scoped) : IDisposable
    {
        public OperationContext Context => cx;
        public void Dispose()
        {
            if(scoped)
                _asyncLocalContext.Value = null;
        }
        
    }
}