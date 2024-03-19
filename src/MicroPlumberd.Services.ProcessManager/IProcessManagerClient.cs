namespace MicroPlumberd.Services.ProcessManagers;

public interface IProcessManagerClient
{
    IPlumber Plumber { get; }
    ICommandBus Bus { get; }

    Task<TProcessManager> GetManager<TProcessManager>(Guid commandRecipientId)
        where TProcessManager : IProcessManager, ITypeRegister;
}