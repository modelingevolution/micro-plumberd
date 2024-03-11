using System.Dynamic;
using EventStore.Client;

namespace MicroPlumberd;


public delegate string SteamNameConvention(Type aggregateType, Guid aggregateId);

public delegate string EventNameConvention(IAggregate aggregate, object evt);

public delegate void MetadataConvention(dynamic metadata, IAggregate aggregate, object evt);
public delegate Uuid EventIdConvention(IAggregate aggregator, object evt);

public interface IConventions
{
    SteamNameConvention GetStreamIdConvention { get; set; }
    EventNameConvention GetEventNameConvention { get; set; }
    MetadataConvention GetMetadataConvention { get; set; }
    EventIdConvention GetEventIdConvention { get; set; }
    object GetMetadata(IAggregate aggregate, object evt, object? metadata);
}
class Conventions : IConventions
{
    public SteamNameConvention GetStreamIdConvention { get; set; } = (aggregateType,id) => $"{aggregateType.Name}-{id}";
    public EventNameConvention GetEventNameConvention { get; set; } = (aggregate, evt) => evt.GetType().Name;
    public MetadataConvention GetMetadataConvention { get; set; }
    public EventIdConvention GetEventIdConvention { get; set; } = (aggregate, evt) => Uuid.NewUuid();

    public object GetMetadata(IAggregate aggregate, object evt, object? metadata)
    {
        ExpandoObject obj = new ExpandoObject();
        GetMetadataConvention?.Invoke(obj, aggregate, evt);
        if (metadata == null) return obj;

        IDictionary<string, object> kv = obj;
        foreach (var i in metadata.GetType().GetProperties().Where(x => x.CanRead))
            kv.Add(i.Name, i.GetGetMethod(false).Invoke(metadata, null));
        return obj;
    }
}