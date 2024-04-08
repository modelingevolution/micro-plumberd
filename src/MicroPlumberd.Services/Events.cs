using System.Net;
using System.Runtime.Serialization;

namespace MicroPlumberd.Services;


[DataContract]
internal record CommandExecuted
{
    [DataMember(Order=1)]
    public Guid CommandId { get; set; }
    [DataMember(Order=2)]
    public TimeSpan Duration { get; set; }
}
[DataContract]
internal record CommandFailed : ICommandFailed
{
    [DataMember(Order = 1)]
    public Guid CommandId { get; set; }
    [DataMember(Order = 2)]
    public TimeSpan Duration { get; set; }
    [DataMember(Order = 3)]
    public string Message { get; set; }
    [DataMember(Order = 4)]
    public HttpStatusCode Code { get; set; }

    public static ICommandFailedEx Create(Guid commandId, string message, TimeSpan duration, HttpStatusCode code, object fault)
    {
        var type = typeof(CommandFailed<>).MakeGenericType(fault.GetType());
        return (ICommandFailedEx)Activator.CreateInstance(type, commandId, message,duration, code, fault)!;
    }
}

interface ICommandFailed
{
    Guid CommandId { get; }
    TimeSpan Duration { get; }
    string Message { get; }
    public HttpStatusCode Code { get; }

}
interface ICommandFailedEx : ICommandFailed
{
    object Fault { get; }

}
[DataContract]
internal record CommandFailed<TFault> : CommandFailed, ICommandFailedEx
{
    public CommandFailed() { }

    object ICommandFailedEx.Fault => this.Fault;
    public CommandFailed(Guid commandId, string message, TimeSpan duration, HttpStatusCode code, TFault Fault)
    {
        this.Fault = Fault;
        this.CommandId = commandId;
        this.Duration = duration;
        this.Message = message;
        this.Code = code;
    }
    [DataMember(Order = 1)]
    public TFault Fault { get; set; }
  
}
