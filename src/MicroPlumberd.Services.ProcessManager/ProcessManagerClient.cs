using MicroPlumberd.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace MicroPlumberd.Services.ProcessManagers
{
    public class ProcessManagerClient : IProcessManagerClient
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IPlumber _plumber;

        public ProcessManagerClient(IServiceProvider serviceProvider, IPlumber plumber, ICommandBus bus)
        {
            _serviceProvider = serviceProvider;
            _plumber = plumber;
            Bus = bus;
        }

        public ICommandBus Bus { get; }
        public IPlumber Plumber { get => _plumber; }

        public async Task<IAsyncDisposable> SubscribeProcessManager<TProcessManager>() where TProcessManager : IProcessManager, ITypeRegister
        {
            ProcessManagerExecutor<TProcessManager> executor = new ProcessManagerExecutor<TProcessManager>(this);
            ProcessManagerExecutor<TProcessManager>.Sender sender =
                new ProcessManagerExecutor<TProcessManager>.Sender(this);
            var c = AsyncDisposableCollection.New();
            c += await Plumber.SubscribeEventHandlerPersistently(sender, $"$ct-{typeof(TProcessManager)}Lookup");
            c += await Plumber.SubscribeEventHandlerPersistently(executor, "");
            return c;
        }
        internal T CreateProcessManager<T>()
        {
            if (_serviceProvider != null) return _serviceProvider.GetService<T>() ?? Activator.CreateInstance<T>();
            return Activator.CreateInstance<T>();
        }

        public async Task<TProcessManager> GetManager<TProcessManager>(Guid commandRecipientId) where TProcessManager : IProcessManager, ITypeRegister
        {
            var lookup = new ProcessManagerExecutor<TProcessManager>.Lookup();

            // This stream is created straight from 
            await _plumber.Rehydrate(lookup, $"{typeof(TProcessManager)}Lookup-{commandRecipientId}");

            var managerId = lookup.GetProcessManagerIdByReceiverId(commandRecipientId) ?? Guid.NewGuid();
            var manager = CreateProcessManager<TProcessManager>();
            if (manager is IIdAware a) a.Id = managerId;

            await _plumber.Rehydrate(manager, $"{typeof(TProcessManager).Name}-{managerId}");

            return manager;
        }
    }
}
