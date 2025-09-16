namespace MicroPlumberd;

public class FaultException<TData> : FaultException
{
    public FaultException(string? message, TData data, int code) : base(message)
    {
        Data = data;
        Code = code;
    }
    public FaultException(TData data, int code) : base(data.ToString())
    {
        Data = data;
        Code = code;
    }

    public TData Data { get; init; }

    public override object GetFaultData() => (object)this.Data;

    public FaultException(TData data) => this.Data = data;
}
public class FaultException : Exception
{
    public int Code { get; init; }
    public FaultException()
    {
    }

    public static FaultException Create(string message, object data, int code)
    {
        return (FaultException)Activator.CreateInstance(typeof(FaultException<>).MakeGenericType(data.GetType()), message, data, code)!;
    }
    public FaultException(string? message) : base(message)
    {
    }
    public FaultException(string? message, int code) : base(message)
    {
        Code = code;
    }

    public virtual object GetFaultData() => null;
}