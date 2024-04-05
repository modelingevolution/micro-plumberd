using System.ComponentModel.DataAnnotations;

namespace MicroPlumberd.Services;

public class CommandHandlerAttributeValidator<T>(ICommandHandler<T> nx, IServiceProvider sp) : ICommandHandler<T>
{
    public Task<object?> Execute(Guid id, T command)
    {
        var validationContext = new ValidationContext(command, sp, null);
        Validator.ValidateObject(command, validationContext);
        return nx.Execute(id, command);
    }

    public Task<object?> Execute(Guid id, object command) => Execute(id, (T)command);
}

class CommandBusAttributeValidator(ICommandBus cb, IServiceProvider sp) : ICommandBus
{
    public async Task SendAsync(Guid recipientId, object command)
    {
        var validationContext = new ValidationContext(command, sp, null);
        Validator.ValidateObject(command, validationContext);
        await cb.SendAsync(recipientId, command);
    }
}