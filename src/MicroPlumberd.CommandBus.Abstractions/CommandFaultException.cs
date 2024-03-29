namespace MicroPlumberd;

public class FaultException<TData> : FaultException
{
    public FaultException(string? message, TData data) : base(message)
    {
        Data = data;
    }

    public TData Data { get; init; }

    public override object GetFaultData() => (object)this.Data;

    public FaultException(TData data) => this.Data = data;
}
public class FaultException : Exception
{
    public FaultException()
    {
    }

    public static FaultException Create(string message, object data)
    {
        return (FaultException)Activator.CreateInstance(typeof(FaultException<>).MakeGenericType(data.GetType()), message, data)!;
    }
    public FaultException(string? message) : base(message)
    {
    }

    public virtual object GetFaultData() => null;
}