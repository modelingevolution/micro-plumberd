using EventStore.Client;
using MicroPlumberd.DirectConnect;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

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

    public static IServiceCollection AddBackgroundServiceIfMissing<TService>(this IServiceCollection services)
        where TService : BackgroundService
    {
        // Check if the service is already added
        var serviceDescriptor = services.FirstOrDefault(descriptor =>
            descriptor.ServiceType == typeof(IHostedService) &&
            descriptor.ImplementationType == typeof(TService));

        // Add the service if it's missing
        if (serviceDescriptor == null)
        {
            services.AddHostedService<TService>();
        }

        return services;
    }
    public static IServiceCollection AddCommandHandler<TCommandHandler>(this IServiceCollection services) where TCommandHandler:ICommandHandler, IServiceTypeRegister
    {
        services.AddBackgroundServiceIfMissing<CommandHandlerService>();
        services.AddSingleton<ICommandHandlerStarter, CommandHandlerStarter<TCommandHandler>>();
        TCommandHandler.RegisterHandlers(services);
        return services;
    }
}