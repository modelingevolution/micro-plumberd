namespace MicroPlumberd.Services;


record CommandExecuted
{
    public Guid CommandId { get; init; }
    public TimeSpan Duration { get; init; }
}

record CommandFailed
{
    public Guid CommandId { get; init; }
    public TimeSpan Duration { get; init; }
    public string Message { get; init; }

    public static ICommandFailedEx Create(Guid commandId, string message, TimeSpan duration, object fault)
    {
        var type = typeof(CommandExecuted<>).MakeGenericType(fault.GetType());
        return (ICommandFailedEx)Activator.CreateInstance(type, commandId, message,duration, fault)!;
    }
}

interface ICommandFailed
{
    Guid CommandId { get; }
    TimeSpan Duration { get; }
    string Message { get; }
    
}
interface ICommandFailedEx : ICommandFailed
{
    object Fault { get; }
}
record CommandExecuted<TFault> : CommandFailed, ICommandFailedEx
{
    public CommandExecuted() { }

    object ICommandFailedEx.Fault => this.Fault;
    public CommandExecuted(Guid commandId, string message, TimeSpan duration, TFault Fault)
    {
        this.Fault = Fault;
        this.CommandId = commandId;
        this.Duration = duration;
        this.Message = message;
    }
    public TFault Fault { get; init; }
  
}
