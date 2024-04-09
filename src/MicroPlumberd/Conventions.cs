using System.Collections.Concurrent;
using System.Dynamic;
using System.Reflection;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Text.Json;
using EventStore.Client;
using Grpc.Core;
using static System.Formats.Asn1.AsnWriter;

namespace MicroPlumberd;

readonly struct SnapshotConverter(Type snapshot)
{
    public bool Convert(string evName, out Type t)
    {
        t = snapshot;
        return true;
    }
}


/// <summary>
/// Represents a delegate that defines a convention for determining the stream category based on the aggregate type.
/// </summary>
/// <param name="aggregateType">The type of the aggregate.</param>
/// <returns>A string representing the stream category.</returns>
public delegate string StreamCategoryConvention(Type aggregateType);
/// <summary>
/// Represents a delegate that defines the convention for generating a steam name based on the aggregate type and aggregate ID.
/// </summary>
/// <param name="aggregateType">The type of the aggregate.</param>
/// <param name="aggregateId">The ID of the aggregate.</param>
/// <returns>A string representing the steam name.</returns>
public delegate string SteamNameConvention(Type aggregateType, object aggregateId);

/// <summary>
/// Represents a delegate that defines a convention for determining the stream category named based on event-type.
/// </summary>
/// <param name="eventType">Type of the event.</param>
/// <returns></returns>
public delegate string StreamNameFromEventConvention(Type eventType, object? id);

/// <summary>
/// Represents a delegate that defines the convention for determining the projection category stream for a given model type.
/// </summary>
/// <param name="type">The type of the model.</param>
/// <returns>The projection category stream.</returns>
public delegate string ProjectionCategoryStreamConvention(Type type);

/// <summary>
/// Represents a delegate that defines the convention for generating event names.
/// </summary>
/// <param name="ownerType">The type of the owner of the event.</param>
/// <param name="evtType">The type of the event.</param>
/// <returns>A string representing the generated event name.</returns>
public delegate string EventNameConvention(Type? ownerType, Type evtType);

/// <summary>
/// Represents a delegate that returns the name of a snapshot event for a given state type.
/// </summary>
/// <param name="stateType">The type of the state.</param>
/// <returns>The name of the snapshot event.</returns>
public delegate string SnapshotEventName(Type stateType);

/// <summary>
/// Represents a policy for creating snapshots of a specific type.
/// </summary>
public delegate ISnapshotPolicy SnapshotPolicyFactory(Type owner);
/// <summary>
/// Represents a delegate that defines a convention for handling metadata in an event.
/// </summary>
/// <param name="metadata">The dynamic metadata associated with the event.</param>
/// <param name="aggregate">The optional aggregate associated with the event.</param>
/// <param name="evt">The event object.</param>
public delegate void MetadataConvention(dynamic metadata, IAggregate? aggregate, object evt);

public delegate void BuildInvocationContext(InvocationContext context, Metadata m);
/// <summary>
/// Represents delegate that creates Uuid from an event and optinally aggregate instance.
/// </summary>
public delegate Uuid EventIdConvention(IAggregate? aggregator, object evt);

/// <summary>
/// Represents a delegate that defines the convention for generating the output stream name based on the model type.
/// </summary>
/// <param name="model">The model type.</param>
/// <returns>The output stream name.</returns>
public delegate string OutputStreamModelConvention(Type model);
/// <summary>
/// Represents a delegate that defines a naming convention for group name used in persistent subscription.
/// </summary>
/// <param name="model">The type of the model.</param>
/// <returns>A string representing the group name for the model.</returns>
public delegate string GroupNameModelConvention(Type model);

/// <summary>
/// Represents a set of read-only conventions used by the MicroPlumberd framework.
/// </summary>
public interface IReadOnlyConventions : IExtension
{
    ProjectionCategoryStreamConvention ProjectionCategoryStreamConvention { get; }
    StreamNameFromEventConvention StreamNameFromEventConvention { get;}
    StreamCategoryConvention GetStreamCategoryConvention { get;  }
    SteamNameConvention GetStreamIdConvention { get; }
    SteamNameConvention GetStreamIdSnapshotConvention { get;  }
    SnapshotEventName SnapshotEventNameConvention { get;  }
    EventNameConvention GetEventNameConvention { get;  }
    BuildInvocationContext BuildInvocationContext { get;  }
    MetadataConvention? MetadataEnrichers { get;  }
    EventIdConvention GetEventIdConvention { get; }
    OutputStreamModelConvention OutputStreamModelConvention { get;  }
    GroupNameModelConvention GroupNameModelConvention { get;  }
    SnapshotPolicyFactory SnapshotPolicyFactoryConvention { get;  }
    StandardMetadataEnricherTypes StandardMetadataEnricherTypes { get;  }
    object GetMetadata(IAggregate? aggregate, object evt, object? metadata);

}
public interface IConventions : IExtension
{
    ProjectionCategoryStreamConvention ProjectionCategoryStreamConvention { get; set; }
    StreamCategoryConvention GetStreamCategoryConvention { get; set; }
    SteamNameConvention GetStreamIdConvention { get; set; }
    SteamNameConvention GetStreamIdSnapshotConvention { get; set; }
    SnapshotEventName SnapshotEventNameConvention { get; set; }
    EventNameConvention GetEventNameConvention { get; set; }
    BuildInvocationContext BuildInvocationContext { get; set; }
    MetadataConvention? MetadataEnrichers { get; set; }
    EventIdConvention GetEventIdConvention { get; set; }
    OutputStreamModelConvention OutputStreamModelConvention { get; set; }
    GroupNameModelConvention GroupNameModelConvention { get; set; }
    SnapshotPolicyFactory SnapshotPolicyFactoryConvention { get; set; }
    StandardMetadataEnricherTypes StandardMetadataEnricherTypes { get; set; }
    object GetMetadata(IAggregate? aggregate, object evt, object? metadata);
    StreamNameFromEventConvention StreamNameFromEventConvention { get; set; }

}

public interface IExtension
{
    T GetExtension<T>() where T : new();
}
class Conventions : IConventions, IReadOnlyConventions
{
    private readonly ConcurrentDictionary<Type,object> _extension = new();
    public T GetExtension<T>() where T : new() => (T)_extension.GetOrAdd(typeof(T), x => new T());
    private StandardMetadataEnricherTypes _standardMetadataEnricherTypes = StandardMetadataEnricherTypes.All;
    public SteamNameConvention GetStreamIdConvention { get; set; }
    public SteamNameConvention GetStreamIdSnapshotConvention { get; set; }
    public SnapshotEventName SnapshotEventNameConvention { get; set; } = t => $"{t.GetFriendlyName()}SnapShotted";
    public StreamCategoryConvention GetStreamCategoryConvention { get; set; } = agg => $"{agg.GetFriendlyName()}";
    public EventNameConvention GetEventNameConvention { get; set; } = (aggregate, evt) => evt.GetFriendlyName();
    public MetadataConvention? MetadataEnrichers { get; set; }
    public BuildInvocationContext BuildInvocationContext { get; set; } = InvocationContext.Build;
    public EventIdConvention GetEventIdConvention { get; set; } = (aggregate, evt) => Uuid.NewUuid();
    public OutputStreamModelConvention OutputStreamModelConvention { get; set; } = OutputStreamFromModel;
    public GroupNameModelConvention GroupNameModelConvention { get; set; } = (t) => t.GetFriendlyName();
    public SnapshotPolicyFactory SnapshotPolicyFactoryConvention { get; set; }
    public ProjectionCategoryStreamConvention ProjectionCategoryStreamConvention { get; set; }
    public StreamNameFromEventConvention StreamNameFromEventConvention { get; set; }
    public StandardMetadataEnricherTypes StandardMetadataEnricherTypes
    {
        get => _standardMetadataEnricherTypes;
        set
        {
            if (_standardMetadataEnricherTypes == value) return;

            AdjustEnrichersFromFlag(StandardMetadataEnricherTypes.Created, value,StandardMetadataEnrichers.CreatedTimeMetadata);
            AdjustEnrichersFromFlag(StandardMetadataEnricherTypes.InvocationContext, value, StandardMetadataEnrichers.InvocationContextMetadata);

            _standardMetadataEnricherTypes = value;
        }
    }

    private void AdjustEnrichersFromFlag(StandardMetadataEnricherTypes flag, StandardMetadataEnricherTypes value, MetadataConvention mth)
    {
        var isOn = value.HasFlag(flag);
        var wasOn = _standardMetadataEnricherTypes.HasFlag(flag);
        if (!(wasOn ^ isOn)) return;
        
        if (wasOn) MetadataEnrichers -= mth;
        else MetadataEnrichers += mth;
    }

    public Conventions()
    {
        MetadataEnrichers += StandardMetadataEnrichers.CreatedTimeMetadata;
        MetadataEnrichers += StandardMetadataEnrichers.InvocationContextMetadata;
        GetStreamIdConvention = (aggregateType,id) => $"{GetStreamCategoryConvention(aggregateType)}-{id}";
        GetStreamIdSnapshotConvention = (aggregateType, id) => $"{GetStreamCategoryConvention(aggregateType)}Snapshot-{id}";
        ProjectionCategoryStreamConvention =(t) => $"$ce-{GetStreamCategoryConvention(t)}";
        StreamNameFromEventConvention = ComputeStreamName;
    }

    private static string ComputeStreamName(Type eventType, object? id)
    {
        var o = eventType.GetCustomAttribute<OutputStreamAttribute>();
        var category = o != null
            ? o.OutputStreamName
            : eventType.Namespace?.Split('.').LastOrDefault(x => x != "Events") ?? "None";

        return id != null ? $"{category}-{id}" : category;
    }

    private static string OutputStreamFromModel(Type model) => model.GetCustomAttribute<OutputStreamAttribute>()?.OutputStreamName ?? model.Name;

    public object GetMetadata(IAggregate? aggregate, object evt, object? metadata)
    {
        ExpandoObject obj = new ExpandoObject();
        MetadataEnrichers?.Invoke(obj, aggregate, evt);
        if (metadata == null) return obj;

        IDictionary<string, object> kv = obj!;
        foreach (var i in metadata.GetType().GetProperties().Where(x => x.CanRead))
        {
            string prop = i.Name
                .Replace("CorrelationId", "$correlationId")
                .Replace("CausationId", "$causationId");
            var value = i.GetGetMethod(false)!.Invoke(metadata, null)!;
            kv[prop] = value;
        }
        return obj;
    }

    
}
[Flags]
public enum StandardMetadataEnricherTypes
{
    None = 0x0,
    Created = 0x1,
    InvocationContext = 0x2,
    All = 0x3
}
static class StandardMetadataEnrichers
{
    public static void CreatedTimeMetadata(dynamic metadata, IAggregate? aggregate, object evt)
    {
        metadata.Created = DateTimeOffset.Now;

    }

    public static void InvocationContextMetadata(dynamic metadata, IAggregate? aggregate, object evt)
    {
        var src = (IDictionary<string, object>)InvocationContext.Current.Value;
        var dst = (IDictionary<string, object>)metadata;
        foreach(var i in src)
            dst.Add(i.Key, i.Value);
    }
}

public class InvocationScope : IDisposable
{
    public InvocationScope() { }

    public InvocationScope(InvocationContext copy) => InvocationContext.Current = copy;
    public InvocationContext Context => InvocationContext.Current;
    public InvocationContext SetCorrelation(Guid correlationId) => Context.SetCorrelation(correlationId);
    public InvocationContext SetCausation(Guid causationId) => Context.SetCausation(causationId);
    public InvocationContext SetUserId(Guid userId) => Context.SetUserId(userId);
    public InvocationContext Set(string key, object value) => Context.Set(key, value);
    public bool ContainsProperty(string propertyName) => Context.ContainsProperty(propertyName);
    public void Dispose() => InvocationContext.Current.Clear();
}

public static class TypeExtensions
{
    public static string GetFriendlyName(this Type type)
    {
        if (!type.IsGenericType)
        {
            return type.Name;
        }

        StringBuilder builder = new StringBuilder();
        string name = type.Name;
        int index = name.IndexOf('`');
        if (index > 0)
        {
            name = name.Substring(0, index);
        }

        builder.Append(name);
        builder.Append('<');

        Type[] genericArguments = type.GetGenericArguments();
        for (int i = 0; i < genericArguments.Length; i++)
        {
            string argumentName = GetFriendlyName(genericArguments[i]);
            if (i > 0)
            {
                builder.Append(", ");
            }
            builder.Append(argumentName);
        }
        builder.Append('>');

        return builder.ToString();
    }
}
public static class MetadataExtensions
{
    public static DateTimeOffset? Created(this Metadata m)
    {

        if (m.Data.TryGetProperty("Created", out var v))
            return DateTimeOffset.Parse(v.GetString()!);
        return null;
    }

    public static long? SnapshotVersion(this Metadata m)
    {
        if (m.Data.TryGetProperty("SnapshotVersion", out var v))
            return v.GetInt64();
        return null;
    }
    public static Guid? CorrelationId(this Metadata m)
    {
        if (m.Data.TryGetProperty("$correlationId", out var v))
            return Guid.Parse(v.GetString()!);
        return null;
    }
    public static Guid? CausationId(this Metadata m)
    {
        if (m.Data.TryGetProperty("$causationId", out var v))
            return Guid.Parse(v.GetString()!);
        return null;
    }
    public static bool TryGetValue<TValue>(this Metadata m, string propertyName, out TValue value)
    {
        if (m.Data.TryGetProperty(propertyName, out var v))
        {
            value = JsonSerializer.Deserialize<TValue>(v);
            return true;
        }

        value = default;
        return false;
    }
}
public class InvocationContext
{
    public InvocationContext SetCorrelation(Guid correlationId) => Set("$correlationId", correlationId);

    public InvocationContext SetCausation(Guid causationId) => Set("$causationId", causationId);
    public InvocationContext SetUserId(Guid userId)
    {
        Value.UserId = userId;
        return this;
    }

    public Guid? CausactionId() => TryGetValue<Guid>("$causationId", out var v) ? v : null;
    
    private static AsyncLocal<InvocationContext> _current = new AsyncLocal<InvocationContext>();
    public static InvocationContext Current
    {
        get => _current.Value ?? (_current.Value = new InvocationContext());
        set => _current.Value = value;
    }

    private readonly ExpandoObject _data;
    private InvocationContext()
    {
        _data = new ExpandoObject();
    }
    private InvocationContext(ExpandoObject data)
    {
        _data = data;
    }

    public dynamic Value => _data;
    
    public InvocationContext Set(string key, object value)
    {
        var dict  = (IDictionary<string, object>)_data!;
        dict[key] = value;
        return this;
    }
    public bool ContainsProperty(string propertyName) => ((IDictionary<string, object>)_data!).ContainsKey(propertyName);

    public bool TryGetValue<TValue>(string propertyName, out TValue value)
    {
        var dict = (IDictionary<string, object>)_data!;
        if (dict.TryGetValue(propertyName, out var v))
        {
            value = (TValue)v;
            return true;
        }

        value = default;
        return false;
    }
    public void Clear()
    {
        IDictionary<string, object> obj = _data!;
        obj.Clear();
    }

    public void ClearCorrelation()
    {
        var dict = (IDictionary<string, object>)_data!;
        dict.Remove("$correlationId");
    }

    public Guid? CorrelationId() => TryGetValue<Guid>("$correlationId", out var v) ? v : null;

    public static void Build(InvocationContext context, Metadata metadata)
    {
        if (metadata.CorrelationId() != null)
            context.SetCorrelation(metadata.CorrelationId()!.Value);
        else context.ClearCorrelation();
        context.SetCausation(metadata.CausationId() != null ? metadata.CausationId()!.Value : metadata.EventId);
    }

    public InvocationContext Clone()
    {
        var dictOriginal = _data as IDictionary<string, object>; // ExpandoObject supports IDictionary
        var dst = new ExpandoObject();
        var dictClone = dst as IDictionary<string, object>;

        // Shallow copy, for deep copy you need a different approach
        foreach (var kvp in dictOriginal) dictClone[kvp.Key] = kvp.Value; 

        return new InvocationContext(dst);
    }
}