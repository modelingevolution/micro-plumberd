using System.Net;
using ProtoBuf;

namespace MicroPlumberd.DirectConnect;

[ProtoContract]
public class HandlerOperationStatus
{
    [ProtoMember(1)]
    public HttpStatusCode Code { get; init; }

    [ProtoMember(2)]
    public string Error { get; init; }

    public static HandlerOperationStatus Ok() => new HandlerOperationStatus() { Code = HttpStatusCode.OK };
    
}