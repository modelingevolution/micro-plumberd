using ProtoBuf;
using System.Net;

namespace MicroPlumberd.DirectConnect;

[ProtoContract]
public record CommandEnvelope<TCommand>
{
    private Guid? _id = null;

    public Guid CommandId
    {
        get
        {
            if (Command is IId i) return i.Uuid;
            return _id ??= Guid.NewGuid();
        }
    }

    [ProtoMember(1)]
    public string StreamId { get; init; }

    [ProtoMember(2)]
    public required TCommand Command { get; init; }

    [ProtoMember(3)]
    public Guid? CorrelationId { get; init; }
}
public static class FaultEnvelope
{
    public static object Create(object faultData, string message)
    {
        return Activator.CreateInstance(typeof(FaultEnvelope<>).MakeGenericType(faultData.GetType()), faultData,
            message)!;
    }
}

public interface IFaultEnvelope
{
    object Data { get; }
    string Error { get;}
    HttpStatusCode Code { get;  }
}

[ProtoContract]
public record FaultEnvelope<TData> : IFaultEnvelope
{
    public FaultEnvelope(TData data, string error)
    {
        Data = data;
        Error = error;
    }

    public FaultEnvelope()
    {
        
    }
    object IFaultEnvelope.Data => this.Data;

    [ProtoMember(1)]
    public required TData Data { get; init; }
    
    [ProtoMember(2)]
    public required string Error { get; init; }

    [ProtoMember(3)]
    public HttpStatusCode Code { get; init; }
}