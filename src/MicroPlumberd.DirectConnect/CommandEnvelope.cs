using ProtoBuf;
using System.Net;

namespace MicroPlumberd.DirectConnect;

/// <summary>
/// Represents an envelope that wraps a command with metadata for distributed command handling.
/// </summary>
/// <typeparam name="TCommand">The type of command being wrapped.</typeparam>
[ProtoContract]
public record CommandEnvelope<TCommand>
{
    private Guid? _id = null;

    /// <summary>
    /// Gets the unique identifier for this command instance.
    /// If the command implements <see cref="IId"/>, returns its UUID; otherwise generates a new GUID.
    /// </summary>
    public Guid CommandId
    {
        get
        {
            if (Command is IId i) return i.Uuid;
            return _id ??= Guid.NewGuid();
        }
    }

    /// <summary>
    /// Gets or initializes the identifier of the stream where this command should be applied.
    /// </summary>
    [ProtoMember(1)]
    public string StreamId { get; init; }

    /// <summary>
    /// Gets or initializes the command to be executed.
    /// </summary>
    [ProtoMember(2)]
    public required TCommand Command { get; init; }

    /// <summary>
    /// Gets or initializes the correlation identifier for tracking related operations across distributed systems.
    /// </summary>
    [ProtoMember(3)]
    public Guid? CorrelationId { get; init; }
}

/// <summary>
/// Factory class for creating fault envelopes with type-erased fault data.
/// </summary>
public static class FaultEnvelope
{
    /// <summary>
    /// Creates a fault envelope from fault data and an error message.
    /// </summary>
    /// <param name="faultData">The fault-specific data to include in the envelope.</param>
    /// <param name="message">The error message describing the fault.</param>
    /// <returns>A fault envelope wrapping the provided data and message.</returns>
    public static object Create(object faultData, string message)
    {
        return Activator.CreateInstance(typeof(FaultEnvelope<>).MakeGenericType(faultData.GetType()), faultData,
            message)!;
    }
}

/// <summary>
/// Represents a fault envelope that contains error information and fault-specific data.
/// </summary>
public interface IFaultEnvelope
{
    /// <summary>
    /// Gets the fault-specific data associated with the error.
    /// </summary>
    object Data { get; }

    /// <summary>
    /// Gets the error message describing the fault.
    /// </summary>
    string Error { get;}

    /// <summary>
    /// Gets the HTTP status code associated with the fault.
    /// </summary>
    HttpStatusCode Code { get;  }
}

/// <summary>
/// Represents a strongly-typed fault envelope that wraps fault data with error information.
/// </summary>
/// <typeparam name="TData">The type of fault-specific data.</typeparam>
[ProtoContract]
public record FaultEnvelope<TData> : IFaultEnvelope
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FaultEnvelope{TData}"/> class.
    /// </summary>
    /// <param name="data">The fault-specific data.</param>
    /// <param name="error">The error message.</param>
    public FaultEnvelope(TData data, string error)
    {
        Data = data;
        Error = error;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FaultEnvelope{TData}"/> class.
    /// Default constructor for deserialization.
    /// </summary>
    public FaultEnvelope()
    {

    }
    object IFaultEnvelope.Data => this.Data;

    /// <summary>
    /// Gets or initializes the fault-specific data.
    /// </summary>
    [ProtoMember(1)]
    public required TData Data { get; init; }

    /// <summary>
    /// Gets or initializes the error message describing the fault.
    /// </summary>
    [ProtoMember(2)]
    public required string Error { get; init; }

    /// <summary>
    /// Gets or initializes the HTTP status code associated with the fault.
    /// </summary>
    [ProtoMember(3)]
    public HttpStatusCode Code { get; init; }
}