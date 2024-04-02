using MicroPlumberd;
using MicroPlumberd.Services;
using Microsoft.Extensions.DependencyInjection;

public class CommandHandlerSpecs<TCommandHandler>(SpecsRoot root) where TCommandHandler :IServiceTypeRegister
{
    private readonly ICommandBus _bus = root.Plumber.Config.ServiceProvider.GetRequiredService<ICommandBus>();

    public Task When<TCommand>(TCommand cmd)
    {
        var subject = root.Conventions.CommandHandlerTypeSubjectConvention(typeof(TCommandHandler));
        var id = root.SubjectPool.GetOrCreate(subject);
        return When(id, cmd);
    }
    public async Task When<TCommand>(Guid recipient, TCommand cmd)
    {
        if (cmd == null) throw new ArgumentNullException($"Command {typeof(TCommand).Name} cannot be null.");
        var subject = root.Conventions.CommandHandlerTypeSubjectConvention(typeof(TCommandHandler));
        root.SubjectPool.Store(subject, recipient);
        try
        {
            await _bus.SendAsync(recipient, cmd);
            root.RegisterStepExecution<TCommand>(StepType.When, cmd);
        }
        catch (Exception ex)
        {
            root.RegisterStepExecutionFailed<TCommand>(StepType.When, ex);
        }
    }
}