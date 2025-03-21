﻿using System.Collections.Concurrent;
using System.Dynamic;
using System.Reflection;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using EventStore.Client;
using Grpc.Core;
using MicroPlumberd.Utils;
using static System.Formats.Asn1.AsnWriter;

namespace MicroPlumberd;

readonly struct SingleTypeConverter(Type snapshot)
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
public delegate string SteamNameConvention(OperationContext context, Type aggregateType, object aggregateId);

/// <summary>
/// Represents a delegate that defines a convention for determining the stream category named based on event-type.
/// </summary>
/// <param name="eventType">Type of the event.</param>
/// <returns></returns>
public delegate string StreamNameFromEventConvention(OperationContext context, Type eventType, object? id);

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
public delegate void MetadataConvention(OperationContext context, dynamic metadata, IAggregate? aggregate, object evt);


/// <summary>
/// Represents delegate that creates Uuid from an event and optinally aggregate instance.
/// </summary>
public delegate Uuid EventIdConvention(OperationContext context, IAggregate? aggregator, object evt);

public delegate Uuid EventIdStateConvention(object state, object id, long? version);

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
    /// <summary>
    /// Gets the projection category stream convention. Used in plumber.Read operations to find out full stream-id from TOwner and Id.
    /// </summary>
    /// <value>
    /// The projection category stream convention.
    /// </value>
    ProjectionCategoryStreamConvention ProjectionCategoryStreamConvention { get; }

    /// <summary>
    /// Gets the stream name from event convention. This is used only in AppendEvent. The default searches for [OutputStreamAttribute]. If not found than the last segment of the namespace is used as category, that is not named "Events". 
    /// </summary>
    /// <value>
    /// The stream name from event convention.
    /// </value>
    StreamNameFromEventConvention StreamNameFromEventConvention { get;}
    /// <summary>
    /// Gets the get stream category convention. 
    /// </summary>
    /// <value>
    /// The get stream category convention.
    /// </value>
    StreamCategoryConvention GetStreamCategoryConvention { get;  }
    /// <summary>
    /// Gets or sets the get stream id convention. The default searches for [OutputStreamAttribute], if not present will return name of class and make it friendly (Generic names will be nicely formated).
    /// Used in:
    /// <list type="bullet">
    /// <item>Rehydrate</item>
    /// <item>Read</item>
    /// <item>SaveChanges</item>
    /// <item>SaveNew</item>
    /// </list>
    /// </summary>
    /// <value>
    /// The get stream identifier convention.
    /// </value>
    SteamNameConvention GetStreamIdConvention { get; }
    /// <summary>
    /// Gets or sets the get stream identifier snapshot convention. The default appends "Snapshot" suffix to the name found in [OutputStreamAttribute], if not present then will append to the name of class.
    /// Used in:
    /// <list type="bullet">
    /// <item>GetSnapshot</item>
    /// <item>AppendSnapshot</item>
    /// </list>
    /// </summary>
    /// <value>
    /// The get stream identifier snapshot convention.
    /// </value>
    SteamNameConvention GetStreamIdSnapshotConvention { get;  }

    /// <summary>
    /// Gets the stream identifier state convention. The default searches for [OutputStreamAttribute], if not present will return name of class and make it friendly (Generic names will be nicely formated).
    /// <list type="bullet">
    /// <item>AppendState</item>
    /// <item>GetState</item>
    /// </list>
    /// </summary>
    /// <value>
    /// The get stream identifier state convention.
    /// </value>
    SteamNameConvention GetStreamIdStateConvention { get; }
    /// <summary>
    /// Gets the snapshot event name convention. It appends to the name of the event suffix: "SnapShotted"
    /// </summary>
    /// <value>
    /// The snapshot event name convention.
    /// </value>
    SnapshotEventName SnapshotEventNameConvention { get;  }
    /// <summary>
    /// Gets the get event name convention. Used almost everywhere an event is saved to EventStoreDB.
    /// </summary>
    /// <value>
    /// The get event name convention.
    /// </value>
    EventNameConvention GetEventNameConvention { get; }
    //BuildInvocationContext BuildInvocationContext { get;  }
    MetadataConvention? MetadataEnrichers { get;  }

    EventIdStateConvention GetEventIdStateConvention { get; }
    EventIdConvention GetEventIdConvention { get; }
    OutputStreamModelConvention OutputStreamModelConvention { get;  }
    GroupNameModelConvention GroupNameModelConvention { get;  }
    SnapshotPolicyFactory SnapshotPolicyFactoryConvention { get;  }
    StandardMetadataEnricherTypes StandardMetadataEnricherTypes { get;  }
    object GetMetadata(OperationContext context, IAggregate? aggregate, object evt, object? metadata);

}
/// <summary>
/// An interface used to configure and provide conventions for Plumber.
/// </summary>
/// <seealso cref="MicroPlumberd.IExtension" />
public interface IConventions : IExtension
{
    /// <summary>
    /// Gets or sets the projection category stream convention. Used in plumber.Read operations to find out full stream-id from TOwner and Id.
    /// </summary>
    /// <value>
    /// The projection category stream convention.
    /// </value>
    ProjectionCategoryStreamConvention ProjectionCategoryStreamConvention { get; set; }
    /// <summary>
    /// Gets or sets the stream category convention. 
    /// </summary>
    /// <value>
    /// The get stream category convention.
    /// </value>
    StreamCategoryConvention GetStreamCategoryConvention { get; set; }
    /// <summary>
    /// Gets or sets the stream id convention. The default searches for [OutputStreamAttribute], if not present will return name of class and make it friendly (Generic names will be nicely formated).
    /// Used in:
    /// <list type="bullet">
    /// <item>Rehydrate</item>
    /// <item>Read</item>
    /// <item>SaveChanges</item>
    /// <item>SaveNew</item>
    /// </list>
    /// </summary>
    /// <value>
    /// The get stream identifier convention.
    /// </value>
    SteamNameConvention GetStreamIdConvention { get; set; }
    /// <summary>
    /// Gets or sets the stream identifier snapshot convention. The default appends "Snapshot" suffix to the name found in [OutputStreamAttribute], if not present then will append to the name of class.
    /// Used in:
    /// <list type="bullet">
    /// <item>GetSnapshot</item>
    /// <item>AppendSnapshot</item>
    /// </list>
    /// </summary>
    /// <value>
    /// The get stream identifier snapshot convention.
    /// </value>
    SteamNameConvention GetStreamIdSnapshotConvention { get; set; }
    /// <summary>
    /// Gets or sets the snapshot event name convention. It appends to the name of the event suffix: "SnapShotted"
    /// </summary>
    /// <value>
    /// The snapshot event name convention.
    /// </value>
    SnapshotEventName SnapshotEventNameConvention { get; set; }
    /// <summary>
    /// Gets or sets the event name convention. Used almost everywhere an event is saved to EventStoreDB.
    /// </summary>
    /// <value>
    /// The get event name convention.
    /// </value>
    EventNameConvention GetEventNameConvention { get; set; }

    //BuildInvocationContext BuildInvocationContext { get; set; }
    MetadataConvention? MetadataEnrichers { get; set; }
    EventIdConvention GetEventIdConvention { get; set; }
    OutputStreamModelConvention OutputStreamModelConvention { get; set; }
    GroupNameModelConvention GroupNameModelConvention { get; set; }
    SnapshotPolicyFactory SnapshotPolicyFactoryConvention { get; set; }
    StandardMetadataEnricherTypes StandardMetadataEnricherTypes { get; set; }
    object GetMetadata(OperationContext context, IAggregate? aggregate, object evt, object? metadata);
    /// <summary>
    /// Gets or sets the stream name from event convention. This is used only in AppendEvent. The default searches for [OutputStreamAttribute]. If not found than the last segment of the namespace is used as category, that is not named "Events". 
    /// </summary>
    /// <value>
    /// The stream name from event convention.
    /// </value>
    StreamNameFromEventConvention StreamNameFromEventConvention { get; set; }

}

public interface IExtension
{
    T GetExtension<T>() where T : new();
}
public static class GuidExtensions
{
    [StructLayout(LayoutKind.Explicit)]
    private struct GuidLongOverlay
    {
        [FieldOffset(0)]
        public Guid Guid;

        [FieldOffset(0)]
        public long Long;
    }

    public static Guid Xor(this in Guid guid, long value)
    {
        var overlay = new GuidLongOverlay { Guid = guid };
        overlay.Long ^= value;
        return overlay.Guid;
    }
}
class Conventions : IConventions, IReadOnlyConventions
{
    private readonly ConcurrentDictionary<Type,object> _extension = new();
    public T GetExtension<T>() where T : new() => (T)_extension.GetOrAdd(typeof(T), x => new T());
    private StandardMetadataEnricherTypes _standardMetadataEnricherTypes = StandardMetadataEnricherTypes.All;
    public SteamNameConvention GetStreamIdConvention { get; set; }
    public SteamNameConvention GetStreamIdSnapshotConvention { get; set; }
    public SteamNameConvention GetStreamIdStateConvention { get; set; }
    public SnapshotEventName SnapshotEventNameConvention { get; set; } = t => $"{t.GetFriendlyName()}SnapShotted";
    public StreamCategoryConvention GetStreamCategoryConvention { get; set; } = OutputStreamOrFriendlyTypeName;
    public EventNameConvention GetEventNameConvention { get; set; } = (aggregate, evt) => evt.GetFriendlyName();
    public MetadataConvention? MetadataEnrichers { get; set; }
    //public BuildInvocationContext BuildInvocationContext { get; set; } = InvocationContext.Build;
    public EventIdConvention GetEventIdConvention { get; set; } = EventIdConvention;
    public EventIdStateConvention GetEventIdStateConvention { get; set; } = EventIdStateConvention;

    private static Uuid EventIdStateConvention(object state, object? id, long? version)
    {
        Guid g = Guid.Empty;
        if (id == null) return Uuid.NewUuid();
        
        if(id is Guid guid)
            g = guid;
        else if (!Guid.TryParse(id.ToString(), out g))
            g = id.ToString().ToGuid();
            
        if(version != null)
            g = g.Xor(version.Value);

        if (g == Guid.Empty) throw new InvalidOperationException("Guid cannot be empty");
        
        return g == Guid.Empty ? Uuid.NewUuid() : Uuid.FromGuid(g);
    }

    private static Uuid EventIdConvention(OperationContext context, IAggregate? aggregate, object evt)
    {
        if (!IdDuckTyping.Instance.TryGetGuidId(evt, out var g)) return Uuid.NewUuid();
        
        if (g == Guid.Empty) throw new InvalidOperationException("Guid cannot be empty");
        return Uuid.FromGuid(g);

    }

    public OutputStreamModelConvention OutputStreamModelConvention { get; set; } = OutputStreamOrFriendlyTypeName;
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
        GetStreamIdConvention = (context,aggregateType,id) => $"{GetStreamCategoryConvention(aggregateType)}-{id}";
        GetStreamIdSnapshotConvention = (context, aggregateType, id) => $"{GetStreamCategoryConvention(aggregateType)}Snapshot-{id}";
        GetStreamIdStateConvention = (context, aggregateType, id) => $"{GetStreamCategoryConvention(aggregateType)}-{id}";
        ProjectionCategoryStreamConvention =(t) => $"$ce-{GetStreamCategoryConvention(t)}";
        StreamNameFromEventConvention = ComputeStreamName;
    }

    private static string ComputeStreamName(OperationContext context, Type eventType, object? id)
    {
        var o = eventType.GetCustomAttribute<OutputStreamAttribute>();
        var category = o != null
            ? o.OutputStreamName
            : eventType.Namespace?.Split('.').LastOrDefault(x => x != "Events") ?? "None";

        return id != null ? $"{category}-{id}" : category;
    }

    private static string OutputStreamOrFriendlyTypeName(Type model) => model.GetCustomAttribute<OutputStreamAttribute>()?.OutputStreamName ?? model.GetFriendlyName();

    public object GetMetadata(OperationContext context, IAggregate? aggregate, object evt, object? metadata)
    {
        ExpandoObject obj = new ExpandoObject();
        MetadataEnrichers?.Invoke(context,obj, aggregate, evt);
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
    

   
    public static void CreatedTimeMetadata(OperationContext cx, dynamic metadata, IAggregate? aggregate, object evt)
    {
        metadata.Created = DateTimeOffset.Now;

    }

    public static void InvocationContextMetadata(OperationContext cx, dynamic metadata, IAggregate? aggregate, object evt) => cx.CopyTo(metadata);
}

//public class InvocationScope : IDisposable
//{
//    public InvocationScope() { }

//    public InvocationScope(InvocationContext copy) => InvocationContext.Current = copy;
//    public InvocationContext Context => InvocationContext.Current;
//    public InvocationContext SetCorrelation(Guid correlationId) => Context.SetCorrelation(correlationId);
//    public InvocationContext SetCausation(Guid causationId) => Context.SetCausation(causationId);
//    public InvocationContext SetUserId(string? userId) => Context.SetUserId(userId);
//    public InvocationContext Set(string key, object value) => Context.Set(key, value);
//    public bool ContainsProperty(string propertyName) => Context.ContainsProperty(propertyName);
//    public void Dispose() => InvocationContext.Current.Clear();
//}

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
    public static string? UserId(this Metadata m)
    {
        if (m.Data.TryGetProperty("UserId", out var v))
            return v.GetString();
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