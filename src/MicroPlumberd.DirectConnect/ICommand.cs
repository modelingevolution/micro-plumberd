using System.Net;
using ModelingEvolution.DirectConnect;
using ProtoBuf;

namespace MicroPlumberd.DirectConnect;



public abstract class ReturnsAttribute(Type returnType) : Attribute
{
    public Type ReturnType { get; init; } = returnType;
} 

[AttributeUsage(AttributeTargets.Class)]
public class ReturnsAttribute<TResult>() : ReturnsAttribute(typeof(TResult)) { }

[ProtoContract]
public class HandlerOperationStatus
{
    [ProtoMember(1)]
    public HttpStatusCode Code { get; init; }
    public static HandlerOperationStatus Ok() => new HandlerOperationStatus() { Code = HttpStatusCode.OK };
    
}