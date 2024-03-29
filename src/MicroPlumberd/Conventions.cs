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


public delegate string StreamCategoryConvention(Type aggregateType);
public delegate string SteamNameConvention(Type aggregateType, Guid aggregateId);

public delegate string ProjectionCategoryStreamConvention(Type aggregateType);

public delegate string EventNameConvention(Type? ownerType, Type evtType);

public delegate void MetadataConvention(dynamic metadata, IAggregate? aggregate, object evt);

public delegate void BuildInvocationContext(InvocationContext context, Metadata m);
public delegate Uuid EventIdConvention(IAggregate? aggregator, object evt);

public delegate string OutputStreamModelConvention(Type model);
public delegate string GroupNameModelConvention(Type model);
public interface IConventions : IExtension
{
    ProjectionCategoryStreamConvention ProjectionCategoryStreamConvention { get; set; }
    StreamCategoryConvention GetStreamCategoryConvention { get; set; }
    SteamNameConvention GetStreamIdConvention { get; set; }
    EventNameConvention GetEventNameConvention { get; set; }
    BuildInvocationContext BuildInvocationContext { get; set; }
    MetadataConvention? MetadataEnrichers { get; set; }
    EventIdConvention GetEventIdConvention { get; set; }
    OutputStreamModelConvention OutputStreamModelConvention { get; set; }
    GroupNameModelConvention GroupNameModelConvention { get; set; }
    StandardMetadataEnricherTypes StandardMetadataEnricherTypes { get; set; }
    object GetMetadata(IAggregate? aggregate, object evt, object? metadata);
}

public interface IExtension
{
    T GetExtension<T>() where T : new();
}
class Conventions : IConventions
{
    private readonly ConcurrentDictionary<Type,object> _extension = new();
    public T GetExtension<T>() where T : new() => (T)_extension.GetOrAdd(typeof(T), x => new T());
    private StandardMetadataEnricherTypes _standardMetadataEnricherTypes = StandardMetadataEnricherTypes.All;
    public SteamNameConvention GetStreamIdConvention { get; set; }
    public StreamCategoryConvention GetStreamCategoryConvention { get; set; } = agg => $"{agg.GetFriendlyName()}";
    public EventNameConvention GetEventNameConvention { get; set; } = (aggregate, evt) => evt.GetFriendlyName();
    public MetadataConvention? MetadataEnrichers { get; set; }
    public BuildInvocationContext BuildInvocationContext { get; set; } = InvocationContext.Build;
    public EventIdConvention GetEventIdConvention { get; set; } = (aggregate, evt) => Uuid.NewUuid();
    public OutputStreamModelConvention OutputStreamModelConvention { get; set; } = OutputStreamFromModel;
    public GroupNameModelConvention GroupNameModelConvention { get; set; } = (t) => t.GetFriendlyName();
    public ProjectionCategoryStreamConvention ProjectionCategoryStreamConvention { get; set; }

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
        ProjectionCategoryStreamConvention =(t) => $"$ce-{GetStreamCategoryConvention(t)}";
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