using System.Reflection;
using MicroPlumberd.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelingEvolution.DirectConnect;

namespace MicroPlumberd.DirectConnect;

/// <summary>
/// Extension methods for configuring MicroPlumberd DirectConnect command services in an <see cref="IServiceCollection"/>.
/// </summary>
public static class ContainerExtensions
{

    /// <summary>
    /// Registers a command invoker for the specified command type.
    /// </summary>
    /// <typeparam name="TCommand">The type of command to register an invoker for.</typeparam>
    /// <param name="services">The service collection to add the invoker to.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddCommandInvoker<TCommand>(this IServiceCollection services) 
    {
        return services.AddCommandInvoker(typeof(TCommand));
    }

    /// <summary>
    /// Registers command invokers for multiple command types.
    /// </summary>
    /// <param name="services">The service collection to add the invokers to.</param>
    /// <param name="commandTypes">The command types to register invokers for.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddCommandInvokers(this IServiceCollection services, params Type[] commandTypes)
    {
        return services.AddCommandInvokers(commandTypes.AsEnumerable());
    }

    /// <summary>
    /// Registers command invokers for multiple command types.
    /// </summary>
    /// <param name="services">The service collection to add the invokers to.</param>
    /// <param name="commandTypes">The collection of command types to register invokers for.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddCommandInvokers(this IServiceCollection services, IEnumerable<Type> commandTypes)
    {
        foreach (var c in commandTypes) services.AddCommandInvoker(c);
        return services;
    }

    /// <summary>
    /// Registers a command invoker for the specified command type, including all return types and fault types.
    /// </summary>
    /// <param name="services">The service collection to add the invoker to.</param>
    /// <param name="commandType">The command type to register an invoker for.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddCommandInvoker(this IServiceCollection services, Type commandType) 
    {
        services.AddClientInvoker(typeof(CommandEnvelope<>).MakeGenericType(commandType), typeof(object));
        var returnTypes = commandType.GetCustomAttributes<ReturnsAttribute>().Select(x => x.ReturnType);
        var faultTypes = commandType.GetCustomAttributes<ThrowsFaultExceptionAttribute>()
            .Select(x => typeof(FaultEnvelope<>).MakeGenericType(x.ThrownType));
        services.AddMessages(returnTypes.Union(faultTypes).Union(Enumerable.Repeat(typeof(HandlerOperationStatus),1)));
        return services;
    }

    /// <summary>
    /// Registers command handlers for all commands handled by the specified command handler type.
    /// This method configures request-response handling, correlation decorators, and message type registration.
    /// </summary>
    /// <typeparam name="TCommandHandler">The command handler type that implements <see cref="IServiceTypeRegister"/>.</typeparam>
    /// <param name="services">The service collection to add the handlers to.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the TypeRegister singleton is not found in the service collection.</exception>
    public static IServiceCollection AddCommandHandler<TCommandHandler>(this IServiceCollection services) where TCommandHandler:IServiceTypeRegister
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