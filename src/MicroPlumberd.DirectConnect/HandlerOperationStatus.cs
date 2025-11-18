using System.Net;
using ProtoBuf;

namespace MicroPlumberd.DirectConnect;

/// <summary>
/// Represents the operational status of a command handler execution.
/// </summary>
[ProtoContract]
public class HandlerOperationStatus
{
    /// <summary>
    /// Gets or initializes the HTTP status code indicating the result of the operation.
    /// </summary>
    [ProtoMember(1)]
    public HttpStatusCode Code { get; init; }

    /// <summary>
    /// Gets or initializes the error message if the operation failed, or null if successful.
    /// </summary>
    [ProtoMember(2)]
    public string Error { get; init; }

    /// <summary>
    /// Creates a successful operation status with HTTP 200 OK.
    /// </summary>
    /// <returns>A <see cref="HandlerOperationStatus"/> indicating success.</returns>
    public static HandlerOperationStatus Ok() => new HandlerOperationStatus() { Code = HttpStatusCode.OK };

}