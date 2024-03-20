using MicroPlumberd.Services;
using MicroPlumberd.Services.ProcessManagers;
using MicroPlumberd.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace MicroPlumberd
{
    sealed class ProcessManagerService(IPlumber plumber, IEnumerable<IProcessManagerStarter> starters)
        : BackgroundService
    {
        public override void Dispose()
        {
            Task.WaitAll(starters.Select(x => x.DisposeAsync().AsTask()).ToArray());
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            foreach (var i in starters)
                await i.Start();
        }
    }
    public static class ContainerExtensions
    {
        public static IServiceCollection AddProcessManager<TProcessManager>(this IServiceCollection services) where TProcessManager : IProcessManager, ITypeRegister
        {
            services.TryAddSingleton<IProcessManagerClient, ProcessManagerClient>();
            services.AddSingleton<IProcessManagerStarter, CommandHandlerStarter<TProcessManager>>();
            services.AddBackgroundServiceIfMissing<ProcessManagerService>();
            return services;
        }
    }

    interface IProcessManagerStarter : IAsyncDisposable
    {
        Task Start();
    }
    class CommandHandlerStarter<TProcessManager>(IProcessManagerClient client) : IProcessManagerStarter where TProcessManager : IProcessManager, ITypeRegister
    {
        private IAsyncDisposable _sub;

        public async Task Start()
        {
            _sub = await client.SubscribeProcessManager<TProcessManager>();
        }

        public async ValueTask DisposeAsync()
        {
            await _sub.DisposeAsync();
        }
    }
}

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
            c += await Plumber.SubscribeEventHandlerPersistently(sender, $"{typeof(TProcessManager).Name}Outbox", ensureOutputStreamProjection:true);
            c += await Plumber.SubscribeEventHandlerPersistently(executor, $"{typeof(TProcessManager).Name}Inbox", ensureOutputStreamProjection:true);
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
            await _plumber.Rehydrate(lookup, $"{typeof(TProcessManager).Name}Lookup-{commandRecipientId}");

            var managerId = lookup.GetProcessManagerIdByReceiverId(commandRecipientId) ?? Guid.NewGuid();
            var manager = CreateProcessManager<TProcessManager>();
            if (manager is IIdAware a) a.Id = managerId;

            await _plumber.Rehydrate(manager, $"{typeof(TProcessManager).Name}-{managerId}");

            return manager;
        }
    }
}
