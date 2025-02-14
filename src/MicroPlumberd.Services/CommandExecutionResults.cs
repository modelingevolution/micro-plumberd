using System.Net;

namespace MicroPlumberd.Services;

public class CommandExecutionResults 
{
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

    public HttpStatusCode ErrorCode { get; private set; }


    public string ErrorMessage { get; private set; }
    public object? ErrorData { get; private set; }
    public bool IsSuccess { get; private set; }
    public TaskCompletionSource<bool> IsReady { get; private set; } = new TaskCompletionSource<bool>();
    
}