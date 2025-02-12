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
    public async Task SendAsync(object recipientId, object command, CancellationToken token = default)
    {
        var validationContext = new ValidationContext(command, sp, null);
        Validator.ValidateObject(command, validationContext, true);
        await cb.SendAsync(recipientId, command, token);
    }

    public Task QueueAsync(object recipientId, object command, CancellationToken token = default)
    {
        var validationContext = new ValidationContext(command, sp, null);
        Validator.ValidateObject(command, validationContext, true);
        return cb.QueueAsync(recipientId, command, token);
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}