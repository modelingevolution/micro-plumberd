using System.Collections.Concurrent;
using System.Dynamic;
using System.Reflection;
using System.Reflection.Metadata.Ecma335;
using System.Text.Json;
using EventStore.Client;

namespace MicroPlumberd;


public delegate string SteamNameConvention(Type aggregateType, Guid aggregateId);

public delegate string EventNameConvention(IAggregate? aggregate, object evt);

public delegate void MetadataConvention(dynamic metadata, IAggregate? aggregate, object evt);
public delegate Uuid EventIdConvention(IAggregate? aggregator, object evt);

public delegate string OutputStreamModelConvention(Type model);
public delegate string GroupNameModelConvention(Type model);
public interface IConventions
{
    T GetExtension<T>() where T:new();
    SteamNameConvention GetStreamIdConvention { get; set; }
    EventNameConvention GetEventNameConvention { get; set; }
    MetadataConvention? MetadataEnrichers { get; set; }
    EventIdConvention GetEventIdConvention { get; set; }
    OutputStreamModelConvention OutputStreamModelConvention { get; set; }
    GroupNameModelConvention GroupNameModelConvention { get; set; }
    StandardMetadataEnricherTypes StandardMetadataEnricherTypes { get; set; }
    object GetMetadata(IAggregate? aggregate, object evt, object? metadata);
}
class Conventions : IConventions
{
    private readonly ConcurrentDictionary<Type,object> _extension = new();
    public T GetExtension<T>() where T : new() => (T)_extension.GetOrAdd(typeof(T), x => new T());
    private StandardMetadataEnricherTypes _standardMetadataEnricherTypes = StandardMetadataEnricherTypes.All;
    public SteamNameConvention GetStreamIdConvention { get; set; } = (aggregateType,id) => $"{aggregateType.Name}-{id}";
    public EventNameConvention GetEventNameConvention { get; set; } = (aggregate, evt) => evt.GetType().Name;
    public MetadataConvention? MetadataEnrichers { get; set; }
    public EventIdConvention GetEventIdConvention { get; set; } = (aggregate, evt) => Uuid.NewUuid();
    public OutputStreamModelConvention OutputStreamModelConvention { get; set; } = OutputStreamFromModel;
    public GroupNameModelConvention GroupNameModelConvention { get; set; } = (t) => t.Name;

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
    }

    private static string OutputStreamFromModel(Type model) => model.GetCustomAttribute<OutputStreamAttribute>()?.OutputStreamName ?? model.Name;

    public object GetMetadata(IAggregate? aggregate, object evt, object? metadata)
    {
        ExpandoObject obj = new ExpandoObject();
        MetadataEnrichers?.Invoke(obj, aggregate, evt);
        if (metadata == null) return obj;

        IDictionary<string, object> kv = obj!;
        foreach (var i in metadata.GetType().GetProperties().Where(x => x.CanRead))
            kv.Add(i.Name, i.GetGetMethod(false)!.Invoke(metadata, null)!);
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
    public InvocationContext Context => InvocationContext.Current;
    public InvocationContext SetCorrelation(Guid correlationId) => Context.SetCorrelation(correlationId);
    public InvocationContext SetCausation(Guid causationId) => Context.SetCausation(causationId);
    public InvocationContext SetUserId(Guid userId) => Context.SetUserId(userId);
    public InvocationContext Set(string key, object value) => Context.Set(key, value);
    public bool ContainsProperty(string propertyName) => Context.ContainsProperty(propertyName);
    public void Dispose() => InvocationContext.Current.Clear();
}

public static class MetadataExtensions
{
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
    public static InvocationContext Current => _current.Value ?? (_current.Value = new InvocationContext());
    private readonly ExpandoObject _data = new ExpandoObject();
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
}