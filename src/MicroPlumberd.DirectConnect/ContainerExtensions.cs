using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using ModelingEvolution.DirectConnect;

namespace MicroPlumberd.DirectConnect;

public static class ContainerExtensions
{

    public static IServiceCollection AddCommandInvoker<TCommand>(this IServiceCollection services) 
    {
        return services.AddCommandInvoker(typeof(TCommand));
    }
    
    public static IServiceCollection AddCommandInvokers(this IServiceCollection services, params Type[] commandTypes)
    {
        return services.AddCommandInvokers(commandTypes.AsEnumerable());
    }
    public static IServiceCollection AddCommandInvokers(this IServiceCollection services, IEnumerable<Type> commandTypes)
    {
        foreach (var c in commandTypes) services.AddCommandInvoker(c);
        return services;
    }
    public static IServiceCollection AddCommandInvoker(this IServiceCollection services, Type commandType) 
    {
        services.AddClientInvoker(typeof(CommandEnvelope<>).MakeGenericType(commandType), typeof(object));
        var returnTypes = commandType.GetCustomAttributes<ReturnsAttribute>().Select(x => x.ReturnType);
        var faultTypes = commandType.GetCustomAttributes<ThrowsFaultExceptionAttribute>().Select(x => x.ThrownType);
        services.AddMessages(returnTypes.Union(faultTypes).Union(Enumerable.Repeat(typeof(HandlerOperationStatus),1)));
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