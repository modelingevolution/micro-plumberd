namespace MicroPlumberd;

public class CommandFaultException<TData> : CommandFaultException
{
    public CommandFaultException(string? message, TData data) : base(message)
    {
        Data = data;
    }

    public TData Data { get; init; }

    public override object GetFaultData() => (object)this.Data;

    public CommandFaultException(TData data) => this.Data = data;
}
public class CommandFaultException : Exception
{
    public CommandFaultException()
    {
    }

    public static CommandFaultException Create(string message, object data)
    {
        return (CommandFaultException)Activator.CreateInstance(typeof(CommandFaultException<>).MakeGenericType(data.GetType()), message, data)!;
    }
    public CommandFaultException(string? message) : base(message)
    {
    }

    public virtual object GetFaultData() => null;
}