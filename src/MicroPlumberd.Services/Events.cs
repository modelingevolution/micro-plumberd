using System.Net;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Text.Json;

namespace MicroPlumberd.Services;

/// <summary>
/// Represents a source that provides command identification.
/// </summary>
interface ICommandSource
{
    /// <summary>
    /// Gets the unique identifier of the command.
    /// </summary>
    public Guid CommandId { get; }
}

/// <summary>
/// Represents a successfully executed command event.
/// </summary>
[DataContract]
internal record CommandExecuted : ICommandSource
{
    /// <summary>
    /// Gets or sets the unique identifier of the executed command.
    /// </summary>
    [DataMember(Order=1)]
    public Guid CommandId { get; set; }
    /// <summary>
    /// Gets or sets the duration of command execution.
    /// </summary>
    [DataMember(Order=2)]
    public TimeSpan Duration { get; set; }
}
/// <summary>
/// Represents a failed command execution event.
/// </summary>
[DataContract]
internal record CommandFailed : ICommandFailed
{
    /// <summary>
    /// Gets or sets the unique identifier of the failed command.
    /// </summary>
    [DataMember(Order = 1)]
    public Guid CommandId { get; set; }
    /// <summary>
    /// Gets or sets the duration of command execution before it failed.
    /// </summary>
    [DataMember(Order = 2)]
    public TimeSpan Duration { get; set; }
    /// <summary>
    /// Gets or sets the error message describing why the command failed.
    /// </summary>
    [DataMember(Order = 3)]
    public string Message { get; set; }
    /// <summary>
    /// Gets or sets the HTTP status code associated with the failure.
    /// </summary>
    [DataMember(Order = 4)]
    public HttpStatusCode Code { get; set; }

    /// <summary>
    /// Creates a strongly-typed command failed event with fault data.
    /// </summary>
    /// <param name="commandId">The unique identifier of the failed command.</param>
    /// <param name="message">The error message.</param>
    /// <param name="duration">The duration of command execution before it failed.</param>
    /// <param name="code">The HTTP status code.</param>
    /// <param name="fault">The fault data object.</param>
    /// <returns>A command failed event with typed fault data.</returns>
    public static ICommandFailedEx Create(Guid commandId, string message, TimeSpan duration, HttpStatusCode code, object fault)
    {
        var type = typeof(CommandFailed<>).MakeGenericType(fault.GetType());
        return (ICommandFailedEx)Activator.CreateInstance(type, commandId, message,duration, code, fault)!;
    }
}

/// <summary>
/// Represents a failed command execution with error information.
/// </summary>
interface ICommandFailed : ICommandSource
{
    /// <summary>
    /// Gets the duration of command execution before it failed.
    /// </summary>
    TimeSpan Duration { get; }
    /// <summary>
    /// Gets the error message describing why the command failed.
    /// </summary>
    string Message { get; }
    /// <summary>
    /// Gets the HTTP status code associated with the failure.
    /// </summary>
    public HttpStatusCode Code { get; }

}
/// <summary>
/// Represents a failed command execution with additional fault data.
/// </summary>
interface ICommandFailedEx : ICommandFailed
{
    /// <summary>
    /// Gets the fault data object containing detailed error information.
    /// </summary>
    object Fault { get; }

}
/// <summary>
/// Represents a failed command execution with strongly-typed fault data.
/// </summary>
/// <typeparam name="TFault">The type of fault data.</typeparam>
[DataContract]
internal record CommandFailed<TFault> : CommandFailed, ICommandFailedEx
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CommandFailed{TFault}"/> class.
    /// </summary>
    public CommandFailed() { }

    /// <inheritdoc/>
    object ICommandFailedEx.Fault => this.Fault;
    /// <summary>
    /// Initializes a new instance of the <see cref="CommandFailed{TFault}"/> class with the specified values.
    /// </summary>
    /// <param name="commandId">The unique identifier of the failed command.</param>
    /// <param name="message">The error message.</param>
    /// <param name="duration">The duration of command execution before it failed.</param>
    /// <param name="code">The HTTP status code.</param>
    /// <param name="Fault">The strongly-typed fault data.</param>
    public CommandFailed(Guid commandId, string message, TimeSpan duration, HttpStatusCode code, TFault Fault)
    {
        this.Fault = Fault;
        this.CommandId = commandId;
        this.Duration = duration;
        this.Message = message;
        this.Code = code;
    }
    /// <summary>
    /// Gets or sets the strongly-typed fault data.
    /// </summary>
    [DataMember(Order = 1)]
    public TFault Fault { get; set; }

}
