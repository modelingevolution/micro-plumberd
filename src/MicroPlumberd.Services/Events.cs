using System.Net;

namespace MicroPlumberd.Services;


record CommandExecuted
{
    public Guid CommandId { get; init; }
    public TimeSpan Duration { get; init; }
}

record CommandFailed : ICommandFailed
{
    public Guid CommandId { get; init; }
    public TimeSpan Duration { get; init; }
    public string Message { get; init; }
    public HttpStatusCode Code { get; init; }

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
record CommandFailed<TFault> : CommandFailed, ICommandFailedEx
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
    public TFault Fault { get; init; }
  
}
