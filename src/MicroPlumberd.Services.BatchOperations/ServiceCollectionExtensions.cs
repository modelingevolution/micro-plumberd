using Microsoft.Extensions.DependencyInjection;
using MicroPlumberd.Services;

namespace MicroPlumberd.Services.BatchOperations;

/// <summary>
/// Extension methods for registering BatchOperations services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds BatchOperations services to the service collection.
    /// Uses the specified type to derive application context information.
    /// </summary>
    /// <typeparam name="TApp">Type used to derive application metadata (assembly name, version).</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="enableOrphanDetection">Whether to enable orphan detection background service.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddBatchOperations<TApp>(
        this IServiceCollection services,
        bool enableOrphanDetection = false)
    {
        // Register AppContextProvider using the specified type
        services.AddSingleton<IAppContextProvider, AppContextProvider<TApp>>();

        // Register BatchOperationModel as singleton event handler (in-memory read model)
        services.AddSingletonEventHandler<BatchOperationModel>();

        // Register BatchOperationService
        services.AddScoped<BatchOperationService>();

        if (enableOrphanDetection)
        {
            services.AddHostedService<OrphanDetector>();
        }

        return services;
    }

    /// <summary>
    /// Adds BatchOperations services using a custom IAppContextProvider.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="enableOrphanDetection">Whether to enable orphan detection background service.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// You must register IAppContextProvider before calling this method.
    /// </remarks>
    public static IServiceCollection AddBatchOperations(
        this IServiceCollection services,
        bool enableOrphanDetection = false)
    {
        // Register BatchOperationModel as singleton event handler (in-memory read model)
        services.AddSingletonEventHandler<BatchOperationModel>();

        // Register BatchOperationService
        services.AddScoped<BatchOperationService>();

        if (enableOrphanDetection)
        {
            services.AddHostedService<OrphanDetector>();
        }

        return services;
    }
}
