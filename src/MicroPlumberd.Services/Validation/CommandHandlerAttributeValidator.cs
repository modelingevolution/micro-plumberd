using System.ComponentModel.DataAnnotations;

namespace MicroPlumberd.Services;

public class CommandHandlerAttributeValidator<T>(ICommandHandler<T> nx, IServiceProvider sp) : ICommandHandler<T>
{
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

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}