using System.ComponentModel.DataAnnotations;

namespace MicroPlumberd.Services;

/// <summary>
/// A decorator for command handlers that validates commands using data annotations before execution.
/// </summary>
/// <typeparam name="T">The type of command to validate and handle.</typeparam>
public class CommandHandlerAttributeValidator<T>(ICommandHandler<T> nx, IServiceProvider sp) : ICommandHandler<T>
{
    /// <summary>
    /// Validates the command using data annotations, then executes it if validation passes.
    /// </summary>
    /// <param name="id">The recipient ID.</param>
    /// <param name="command">The command to validate and execute.</param>
    /// <returns>A task representing the asynchronous operation, with an optional result value.</returns>
    /// <exception cref="ValidationException">Thrown when the command fails validation.</exception>
    public Task<object?> Execute(string id, T command)
    {
        var validationContext = new ValidationContext(command, sp, null);
        Validator.ValidateObject(command, validationContext);
        return nx.Execute(id, command);
    }

}

class CommandBusAttributeValidator(ICommandBus cb, IServiceProvider sp) : ICommandBus
{
    public async Task SendAsync(object recipientId, object command, TimeSpan? timeout = null, bool fireAndForget = false,  CancellationToken token = default)
    {
        var validationContext = new ValidationContext(command, sp, null);
        Validator.ValidateObject(command, validationContext, true);
        await cb.SendAsync(recipientId, command, timeout,fireAndForget, token);
    }

    public Task QueueAsync(object recipientId, object command, TimeSpan? timeout = null, bool fireAndForget = true, CancellationToken token = default)
    {
        var validationContext = new ValidationContext(command, sp, null);
        Validator.ValidateObject(command, validationContext, true);
        return cb.QueueAsync(recipientId, command, timeout, fireAndForget, token);
    }
    public void Dispose(){}
    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}