using System.Net;

namespace MicroPlumberd.Services;

/// <summary>
/// Represents the results of a command execution, tracking success, failure, and error information.
/// </summary>
public class CommandExecutionResults
{
    /// <summary>
    /// Handles command execution result events and updates the execution state accordingly.
    /// </summary>
    /// <param name="m">The metadata associated with the event.</param>
    /// <param name="ev">The event to handle, expected to be a command execution result.</param>
    /// <returns>True if the event was handled as a command result; otherwise, false.</returns>
    public async ValueTask<bool> Handle(Metadata m, object ev)
    {
        switch (ev)
        {
            case CommandExecuted ce:
            {
                IsSuccess = true;
                IsReady.SetResult(true);
                return true;
            }
            case ICommandFailedEx ef:
            {
                IsSuccess = false;
                ErrorMessage = ef.Message;
                ErrorData = ef.Fault;
                ErrorCode = ef.Code;
                IsReady.SetResult(true);
                return true;
            }
            case ICommandFailed cf:
            {
                IsSuccess = false;
                ErrorMessage = cf.Message;
                ErrorCode = cf.Code;
                IsReady.SetResult(true);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets the HTTP status code associated with a failed command execution.
    /// </summary>
    public HttpStatusCode ErrorCode { get; private set; }

    /// <summary>
    /// Gets the error message associated with a failed command execution.
    /// </summary>
    public string ErrorMessage { get; private set; }

    /// <summary>
    /// Gets the error data (fault object) associated with a failed command execution.
    /// </summary>
    public object? ErrorData { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the command execution was successful.
    /// </summary>
    public bool IsSuccess { get; private set; }

    /// <summary>
    /// Gets the task completion source that signals when the command execution result is ready.
    /// </summary>
    public TaskCompletionSource<bool> IsReady { get; private set; } = new TaskCompletionSource<bool>();

}