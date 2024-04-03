namespace MicroPlumberd;

[AttributeUsage(AttributeTargets.Class)]
public class AggregateAttribute : Attribute { }


[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public class AcceptedTypeAttribute : Attribute
{
    public AcceptedTypeAttribute(Type acceptedType)
    {
        AcceptedType = acceptedType;
    }

    public Type AcceptedType { get; init; }
}

[AttributeUsage(AttributeTargets.Class)]
public class OutputStreamAttribute : Attribute
{
    public OutputStreamAttribute(string outputStreamName)
    {
        if (string.IsNullOrWhiteSpace(outputStreamName))
            throw new ArgumentException(nameof(outputStreamName));
        this.OutputStreamName = outputStreamName;
    }

    public string OutputStreamName { get; }
}