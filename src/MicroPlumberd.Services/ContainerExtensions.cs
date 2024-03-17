using EventStore.Client;
using MicroPlumberd.DirectConnect;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MicroPlumberd.Services;

public static class ContainerExtensions
{
    public static IServiceCollection AddPlumberd(this IServiceCollection collection,
        EventStoreClientSettings? settings = null, Action<IPlumberConfig>? configure = null)
    {
        collection.AddSingleton(sp => Plumber.Create(settings, x =>
        {
            configure?.Invoke(x);
            x.ServiceProvider = sp;
        }));
        collection.TryAddSingleton<ICommandBus, CommandBus>();
        return collection;
    }
    
    public static IServiceCollection AddCommandHandler<TCommandHandler>(this IServiceCollection services) where TCommandHandler:ICommandHandler, IServiceTypeRegister
    {
        services.AddHostedService<CommandHandlerService>();
        services.AddSingleton<ICommandHandlerStarter, CommandHandlerStarter<TCommandHandler>>();
        TCommandHandler.RegisterHandlers(services);
        return services;
    }
}