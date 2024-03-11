using Microsoft.Extensions.DependencyInjection;
using ModelingEvolution.DirectConnect;

namespace MicroPlumberd.DirectConnect;

public static class ContainerExtensions
{
    public static IServiceCollection AddCommandInvoker<TCommand>(this IServiceCollection services) where TCommand : ICommand
    {
        services.AddClientInvoker<CommandEnvelope<TCommand>,object>();
        return services;
    }

       
    public static IServiceCollection AddCommandHandler<TCommandHandler>(this IServiceCollection services) where TCommandHandler:IApiTypeRegister
    {
        foreach (var cmdType in TCommandHandler.CommandTypes)
        {
            services.AddRequestResponse(typeof(CommandEnvelope<>).MakeGenericType(cmdType), typeof(object));
            var cmdEvnType = typeof(CommandEnvelope<>).MakeGenericType(cmdType);

            var serviceType = typeof(IRequestHandler<,>).MakeGenericType(cmdEvnType, typeof(object));
            services.AddSingleton(serviceType, typeof(CommandHandlerCore<>).MakeGenericType(cmdType));
            services.Decorate(serviceType, typeof(CommandHandlerCorrelationDecorator<>).MakeGenericType(cmdType));
        }
            
        if (!services.TryGetSingleton<TypeRegister>(out var service))
            throw new InvalidOperationException();

        service!.Index(TCommandHandler.ReturnTypes)
            .Index(TCommandHandler.FaultTypes);

        TCommandHandler.RegisterHandlers(services);

        return services;
    }
}